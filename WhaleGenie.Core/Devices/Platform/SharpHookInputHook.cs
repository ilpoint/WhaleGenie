using System;
using System.Threading;
using System.Threading.Tasks;
using SharpHook;
using SharpHook.Data;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The process's keyboard and mouse hook, backed by the hook library: it installs the library's
/// hook, turns what that reports into <see cref="IInputHook"/>'s own events, and carries a
/// handler's answer about swallowing an input back to the library.
/// </summary>
internal sealed class SharpHookInputHook : IInputHook
{
    private readonly IGlobalHook _hook;
    private readonly Task _running;
    private readonly ManualResetEventSlim _installed = new(false);

    internal SharpHookInputHook()
    {
        _hook = new SimpleGlobalHook();
        _hook.HookEnabled += OnInstalled;
        _hook.KeyPressed += OnKeyDown;
        _hook.KeyReleased += OnKeyUp;
        _hook.MousePressed += OnMouseDown;
        _hook.MouseReleased += OnMouseUp;

        // A move while a button is held comes as a drag in addition to the plain move, so both
        // are reported; whoever listens sorts out that it was the same place twice.
        _hook.MouseMoved += OnMoved;
        _hook.MouseDragged += OnMoved;
        _hook.MouseMovedRelative += OnMovedRelative;
        _hook.MouseDraggedRelative += OnMovedRelative;
        _hook.MouseWheel += OnWheel;

        _running = _hook.RunAsync(GlobalHookType.All, useBackgroundThread: true);
        _running.ContinueWith(
            static task => _ = task.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);

        // Installing the hook takes a moment; waiting here is what keeps the first thing a
        // recording is asked to catch from being missed.
        _installed.Wait(TimeSpan.FromSeconds(2));
    }

    public event Action<KeyHookEvent>? KeyDown;

    public event Action<KeyHookEvent>? KeyUp;

    public event Action<MouseHookEvent>? MouseDown;

    public event Action<MouseHookEvent>? MouseUp;

    public event Action<MouseMovedHookEvent>? Moved;

    public event Action<MouseMovedRelativeHookEvent>? MovedRelative;

    public event Action<WheelHookEvent>? Wheel;

    /// <summary>True while the hook thread is still running.</summary>
    internal bool IsAlive => !_running.IsFaulted && !_running.IsCompleted;

    private void OnKeyDown(object? sender, KeyboardHookEventArgs e)
    {
        var raised = HookTranslation.KeyDown(e);
        KeyDown?.Invoke(raised);
        HookTranslation.Swallow(raised.Suppress, e);
    }

    private void OnKeyUp(object? sender, KeyboardHookEventArgs e)
    {
        var raised = HookTranslation.KeyUp(e);
        KeyUp?.Invoke(raised);
        HookTranslation.Swallow(raised.Suppress, e);
    }

    private void OnMouseDown(object? sender, MouseHookEventArgs e)
    {
        if (HookTranslation.MouseDown(e) is not { } raised)
        {
            return;
        }

        MouseDown?.Invoke(raised);
        HookTranslation.Swallow(raised.Suppress, e);
    }

    private void OnMouseUp(object? sender, MouseHookEventArgs e)
    {
        if (HookTranslation.MouseUp(e) is not { } raised)
        {
            return;
        }

        MouseUp?.Invoke(raised);
        HookTranslation.Swallow(raised.Suppress, e);
    }

    private void OnMoved(object? sender, MouseHookEventArgs e)
        => Moved?.Invoke(HookTranslation.Moved(e));

    private void OnMovedRelative(object? sender, MouseHookEventArgs e)
        => MovedRelative?.Invoke(HookTranslation.MovedRelative(e));

    private void OnWheel(object? sender, MouseWheelHookEventArgs e)
    {
        var raised = HookTranslation.Wheel(e);
        Wheel?.Invoke(raised);
        HookTranslation.Swallow(raised.Suppress, e);
    }

    private void OnInstalled(object? sender, HookEventArgs e) => _installed.Set();
}
