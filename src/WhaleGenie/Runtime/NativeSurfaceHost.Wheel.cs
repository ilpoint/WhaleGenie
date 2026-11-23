using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using WhaleGenie.Contracts;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.Views;

namespace WhaleGenie.Runtime;

// The wheel is a gesture of its own beside the menu: the hook reports presses, the state machine
// answers them, and this partial owns the window that shows the sectors. It reads the project and
// the system switch directly, so it keeps working beside the web shell.
internal sealed partial class NativeSurfaceHost
{
    private WheelGesture? _wheel;
    private WheelMenuState _wheelState = new();

    /// <summary>The frame the open wheel was drawn with; the hit test reads it.</summary>
    private WheelRuntimeLayout? _wheelLayout;
    private WheelMetrics _wheelMetrics = WheelGeometry.For(WheelSurface.DefaultDiameter, false);

    /// <summary>Whether a wheel is on screen, copied for the hook thread.</summary>
    private int _wheelHostOpen;

    /// <summary>The project's wheel positions as the hook thread reads them, copied whole.</summary>
    private WheelMenuRules? _wheelGateRules;

    private WheelScene? _wheelPressScene;

    // Frozen for the whole time a wheel is open.
    private WheelScene? _wheelScene;
    private WheelSource _wheelSource = WheelSource.Merged;
    private IReadOnlyList<MacroItem> _wheelMacros = [];
    private WheelMenuRules? _wheelRules;

    private readonly WheelAim _wheelAim = new();
    private int _wheelAimScheduled;
    private long _wheelOpenedAt;
    private long _wheelMovesAtOpen;
    private long _wheelDrawnAtOpen;

    private PixelPoint _wheelOrigin;
    private int _wheelGeneration;

    private void InitializeWheel()
    {
        _wheelState = WheelMenuStore.Load();
        _wheel = new WheelGesture(
            () => Volatile.Read(ref _wheelState).Options.Launcher,
            () => Volatile.Read(ref _wheelState).Options.RepeatKey,
            WheelCanOpen);
        WheelMenuStore.Changed += OnWheelStateChanged;
    }

    private void DisposeWheel()
    {
        WheelMenuStore.Changed -= OnWheelStateChanged;
        CloseWheel();
    }

    /// <summary>Hands the hook thread a copy of the wheel's positions.</summary>
    internal void RefreshWheelGate()
        => Volatile.Write(ref _wheelGateRules, _workspace.Triggers.Wheel.Copy());

    private bool WheelCanOpen()
    {
        _wheelPressScene = null;
        if (!_system.IsEnabled)
        {
            return false;
        }

        var options = Volatile.Read(ref _wheelState).Options;
        if (!options.OnlyInPrograms)
        {
            return true;
        }

        var rules = Volatile.Read(ref _wheelGateRules);
        if (rules is null)
        {
            return false;
        }

        try
        {
            _wheelPressScene = rules.SceneFor(_devices.Windows.Foreground(), _devices.Windows);
        }
        catch
        {
            _wheelPressScene = null;
        }

        return _wheelPressScene is not null;
    }

    private void OnWheelStateChanged()
        => Volatile.Write(ref _wheelState, WheelMenuStore.Load());

    public void WheelMouseDown(MouseHookEvent e)
    {
        try
        {
            if (e.Injected || _wheel is not { } gesture)
            {
                return;
            }

            if (gesture.Down(KeyNames.MouseName(e.Button), e.X, e.Y).Swallow)
            {
                e.Suppress = true;
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel.down", error);
        }
    }

    public void WheelMouseMoved(MouseMovedHookEvent e)
    {
        try
        {
            if (e.Injected || _wheel is not { } gesture)
            {
                return;
            }

            HandleWheel(gesture.Move(e.X, e.Y));
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel.move", error);
        }
    }

    public void WheelMouseUp(MouseHookEvent e)
    {
        try
        {
            if (e.Injected || _wheel is not { } gesture)
            {
                return;
            }

            var answered = gesture.Up(KeyNames.MouseName(e.Button), e.X, e.Y);
            if (answered.Swallow)
            {
                e.Suppress = true;
            }

            if (answered.Action == WheelGestureAction.Replay)
            {
                ReplayClick(e.Button);
                return;
            }

            HandleWheel(answered);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel.up", error);
        }
    }

    private void ReplayClick(HookMouseButton button)
    {
        var name = button switch
        {
            HookMouseButton.Right => "right",
            HookMouseButton.Middle => "middle",
            HookMouseButton.Back => "back",
            HookMouseButton.Forward => "forward",
            _ => "left",
        };

        _ = Task.Run(() =>
        {
            try
            {
                var at = _devices.Input.Cursor;
                _devices.Input.Click(name, at.X, at.Y, 1, 0);
            }
            catch (Exception error)
            {
                AppDiagnostics.Record("wheel.replay", error);
            }
        });
    }

    public void WheelKeyDown(KeyHookEvent e)
    {
        try
        {
            if (_wheel is not { IsOpen: true } gesture)
            {
                return;
            }

            var answered = gesture.Key(KeyNames.Name(e.Key, sided: true));
            if (answered.Action != WheelGestureAction.None)
            {
                e.Suppress = true;
                HandleWheel(answered);
            }
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel.key", error);
        }
    }

    public void WheelScroll(WheelHookEvent e)
    {
        try
        {
            if (Volatile.Read(ref _wheelHostOpen) == 0 || e.Injected
                || e.Axis == HookWheelAxis.Horizontal)
            {
                return;
            }

            var direction = Math.Sign(e.Rotation);
            if (direction == 0)
            {
                return;
            }

            var wanted = direction > 0 ? WheelSource.Global : WheelSource.Program;
            _wheelSource = _wheelSource == wanted ? WheelSource.Merged : wanted;
            PostWheel(() =>
            {
                if (Volatile.Read(ref _wheelHostOpen) == 1)
                {
                    var next = LayoutFor(Volatile.Read(ref _wheelState).Options);
                    _wheelLayout = next;
                    _wheelMetrics = WheelSurface.MetricsOf(next, Volatile.Read(ref _wheelState).Options);
                    _wheelOverlay.SetLayout(next);
                }
            }, "wheel.scroll");
            e.Suppress = true;
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("hook.wheel.scroll", error);
        }
    }

    private WheelRuntimeLayout LayoutFor(WheelOptions options)
        => (_wheelRules ?? new WheelMenuRules()).Resolve(
            _wheelSource == WheelSource.Global ? null : _wheelScene,
            _wheelSource, options.WideOuter, _wheelMacros,
            options.ThirdCapacity, options.HideEmptyOuter);

    private void HandleWheel(WheelGestureEvent answered)
    {
        switch (answered.Action)
        {
            case WheelGestureAction.OpenWheel:
                PostWheel(() => OpenWheel(new PixelPoint(
                    (int)Math.Round(answered.X), (int)Math.Round(answered.Y))), "wheel.open");
                break;
            case WheelGestureAction.Move:
                _wheelAim.Move(answered.X, answered.Y);
                ScheduleWheelAim();
                break;
            case WheelGestureAction.Select:
                PostWheel(() =>
                {
                    RunWheelSlot(answered.Slot);
                    CloseWheel();
                }, "wheel.select");
                break;
            case WheelGestureAction.Repeat:
                PostWheel(() => RunWheelSlot(answered.Slot), "wheel.repeat");
                break;
            case WheelGestureAction.Cancel:
                PostWheel(CloseWheel, "wheel.cancel");
                break;
        }
    }

    private void PostWheel(Action answer, string stage)
    {
        var generation = Volatile.Read(ref _wheelGeneration);
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != Volatile.Read(ref _wheelGeneration))
            {
                return;
            }

            try
            {
                answer();
            }
            catch (Exception error)
            {
                AppDiagnostics.Record(stage, error);
                CloseWheel();
            }
        });
    }

    private void OpenWheel(PixelPoint origin)
    {
        if (Volatile.Read(ref _wheelHostOpen) == 1)
        {
            return;
        }

        var state = Volatile.Read(ref _wheelState);
        if (state.Options.Launcher.Length == 0)
        {
            return;
        }

        try
        {
            _wheelMacros = [.. _workspace.Macros];
            _wheelRules = Volatile.Read(ref _wheelGateRules) ?? _workspace.Triggers.Wheel.Copy();
            _wheelSource = WheelSource.Merged;
            _wheelScene = _wheelPressScene;
            if (_wheelScene is null && !Volatile.Read(ref _wheelState).Options.OnlyInPrograms)
            {
                try
                {
                    _wheelScene = _wheelRules.SceneFor(_devices.Windows.Foreground(), _devices.Windows);
                }
                catch
                {
                    _wheelScene = null;
                }
            }

            var layout = LayoutFor(state.Options);
            _wheelLayout = layout;
            _wheelMetrics = WheelSurface.MetricsOf(layout, state.Options);
            _wheelOverlay.Open(layout, state.Options, new OverlayPoint(origin.X, origin.Y),
                state.Options.TimeoutMs);
            Volatile.Write(ref _wheelHostOpen, 1);
            var centre = _wheelOverlay.Center;
            _wheelOrigin = new PixelPoint(centre.X, centre.Y);
            _wheel?.SetCenter(centre.X, centre.Y, _wheelMetrics);

            _wheelOpenedAt = Stopwatch.GetTimestamp();
            _wheelMovesAtOpen = _wheelAim.Counts.Written;
            _wheelDrawnAtOpen = _wheelAim.Counts.Taken;
            _wheelAim.Clear();
            Volatile.Write(ref _wheelAimScheduled, 0);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("wheel.open", error);
            CloseWheel();
        }
    }

    private void ScheduleWheelAim()
    {
        if (Interlocked.Exchange(ref _wheelAimScheduled, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            Volatile.Write(ref _wheelAimScheduled, 0);
            DrainWheelAim();
        }, DispatcherPriority.Render);
    }

    private void DrainWheelAim()
    {
        if (Volatile.Read(ref _wheelHostOpen) == 0)
        {
            _wheelAim.Clear();
            return;
        }

        if (!_wheelAim.Take(out var x, out var y))
        {
            return;
        }

        try
        {
            UpdateWheelHover(x, y);
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("wheel.hover", error);
            CloseWheel();
        }
    }

    private void UpdateWheelHover(double x, double y)
    {
        if (Volatile.Read(ref _wheelHostOpen) == 0)
        {
            return;
        }

        var origin = _wheelOrigin;
        var chosen = WheelGeometry.At(x - origin.X, y - origin.Y, _wheelMetrics);
        _wheel?.Hover(chosen);
        _wheelOverlay.SetAim(chosen is { } aimed
            ? new SurfaceWheelAim(aimed.Ring.ToString().ToLowerInvariant(), aimed.Index, 0, 0)
            : null,
            new OverlayPoint((int)Math.Round(x), (int)Math.Round(y)));
    }

    private void RunWheelSlot(WheelSlotRef? reference)
    {
        if (reference is not { } chosen
            || _wheelLayout?.At(chosen) is not { Macro: { } macro })
        {
            return;
        }

        _runtime.Triggers.RunOnce(macro);
    }

    public void CloseWheel()
    {
        Volatile.Write(ref _wheelHostOpen, 0);
        _wheelLayout = null;
        _wheelScene = null;
        _wheelRules = null;
        _wheelMacros = [];
        _wheelSource = WheelSource.Merged;

        _wheelGeneration++;
        _wheelAim.Clear();
        NoteWheelFrames();
        _wheelOverlay.Close();
    }

    private void NoteWheelFrames()
    {
        if (_wheelOpenedAt == 0)
        {
            return;
        }

        var openMs = (long)Stopwatch.GetElapsedTime(_wheelOpenedAt).TotalMilliseconds;
        var moves = _wheelAim.Counts.Written - _wheelMovesAtOpen;
        var drawn = _wheelAim.Counts.Taken - _wheelDrawnAtOpen;
        _wheelOpenedAt = 0;
        if (openMs >= 500)
        {
            AppDiagnostics.Note("wheel.frames", $"open={openMs}ms moves={moves} drawn={drawn}");
        }
    }
}
