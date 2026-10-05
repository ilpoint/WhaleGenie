using System;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Data;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// The one keyboard and mouse hook this process installs.
/// </summary>
/// <remarks>
/// Windows does not hand a global hook over cleanly twice: a hook made after an earlier one has
/// been taken down stops receiving events, so a recorder that built its own hook worked for the
/// first couple of recordings and then quietly caught nothing. The hook is therefore installed
/// once and kept for the life of the process, and whatever needs it subscribes while it works.
/// </remarks>
public sealed class GlobalInputHook
{
    private static readonly object Gate = new();
    private static GlobalInputHook? _shared;

    private readonly IGlobalHook _hook;
    private readonly Task _running;
    private readonly ManualResetEventSlim _installed = new(false);

    private GlobalInputHook()
    {
        _hook = new SimpleGlobalHook();
        _hook.HookEnabled += OnInstalled;
        _running = _hook.RunAsync(GlobalHookType.All, useBackgroundThread: true);
        _running.ContinueWith(
            static task => _ = task.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        // Installing the hook takes a moment; waiting here is what keeps the first thing a
        // recording is asked to catch from being missed.
        _installed.Wait(TimeSpan.FromSeconds(2));
    }

    /// <summary>The hook, installed on first use.</summary>
    /// <exception cref="HookException">The hook could not be installed.</exception>
    public static GlobalInputHook Shared
    {
        get
        {
            lock (Gate)
            {
                if (_shared is { IsAlive: true } alive)
                {
                    return alive;
                }

                // A hook that has died is replaced, so a later recording can still work.
                _shared = new GlobalInputHook();
                return _shared;
            }
        }
    }

    /// <summary>The events the hook raises. Subscribe while they are wanted, then unsubscribe.</summary>
    public IGlobalHook Hook => _hook;

    /// <summary>True while the hook thread is still running.</summary>
    public bool IsAlive => !_running.IsFaulted && !_running.IsCompleted;

    private void OnInstalled(object? sender, HookEventArgs e) => _installed.Set();
}
