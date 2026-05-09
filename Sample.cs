using System;
using System.Runtime.InteropServices;
using System.Threading;
using AOT;
using Cysharp.Threading.Tasks;

public class Sample
{
    private static readonly object SyncRoot = new object();
    private static UniTaskCompletionSource<string> _pending;

    /// <summary>
    /// 异步登录。iOS 侧请将 <c>login</c> 导出为接收函数指针，并在适当时机以 UTF-8 字符串回调一次（例如 token / JSON）。
    /// 示例：<c>void login(void (*cb)(const char *data));</c>
    /// </summary>
    public async UniTask<string> LoginAsync(CancellationToken cancellationToken = default)
    {
        UniTaskCompletionSource<string> tcs;
        lock (SyncRoot)
        {
            if (_pending != null)
            {
                throw new InvalidOperationException("LoginAsync 已在进行中，请等待完成后再调用。");
            }

            _pending = new UniTaskCompletionSource<string>();
            tcs = _pending;
        }

        using (cancellationToken.Register(() =>
               {
                   if (TryTakePending(out var pending))
                   {
                       pending.TrySetCanceled(cancellationToken);
                   }
               }))
        {
            try
            {
                login(OnNativeLoginResult);
            }
            catch (Exception ex)
            {
                if (TryTakePending(out var pending))
                {
                    pending.TrySetException(ex);
                }

                throw;
            }

            return await tcs.Task;
        }
    }

    /// <summary>兼容旧入口：不等待结果；需要处理异常请使用 <see cref="LoginAsync"/>。</summary>
    public void Login()
    {
        LoginAsync().Forget();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void NativeCallback(string data);

    [DllImport("__Internal", CallingConvention = CallingConvention.Cdecl, EntryPoint = "login")]
    private static extern void login(NativeCallback callback);

    [MonoPInvokeCallback(typeof(NativeCallback))]
    private static void OnNativeLoginResult(string data)
    {
        if (!TryTakePending(out var pending))
        {
            return;
        }

        CompleteOnMainThread(pending, data ?? string.Empty).Forget();
    }

    private static bool TryTakePending(out UniTaskCompletionSource<string> pending)
    {
        lock (SyncRoot)
        {
            pending = _pending;
            _pending = null;
            return pending != null;
        }
    }

    /// <summary>原生常在非主线程回调；完成 UniTask 前切回 Unity 主线程更安全。</summary>
    private static async UniTaskVoid CompleteOnMainThread(UniTaskCompletionSource<string> pending, string data)
    {
        await UniTask.SwitchToMainThread();
        pending.TrySetResult(data);
    }
}
