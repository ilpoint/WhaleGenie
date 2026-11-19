using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class MainWindow : AppWindow
{
    // The wheel is a gesture of its own beside the menu: the hook reports presses, the state
    // machine answers them, and this file owns the window that shows the sectors. Its state lives
    // here so a task about the wheel does not read the session, menu and surface code as well.
    private WheelGesture? _wheel;
    private WheelMenuWindow? _wheelWindow;
    private WheelMenuState _wheelState = new();

    /// <summary>
    /// Whether a wheel is on screen, copied for the hook thread. It asks this before it scrolls;
    /// reading the window itself from a hook callback would touch Avalonia off its own thread.
    /// </summary>
    private int _wheelHostOpen;

    /// <summary>
    /// The project's wheel positions as the hook thread reads them. The live collection belongs to
    /// the interface thread, so it is copied whenever it changes and replaced whole: a press may
    /// ask whether a program scene matches, and a half-edited scene list is not an answer.
    /// </summary>
    private WheelMenuRules? _wheelGateRules;

    /// <summary>The scene matched by the press that armed the gesture, reused when the wheel opens.</summary>
    private WheelScene? _wheelPressScene;

    // Frozen for the whole time a wheel is open: a window coming forward must not change what a
    // direction means while the pointer is aiming at it.
    private WheelScene? _wheelScene;
    private WheelSource _wheelSource = WheelSource.Merged;
    private IReadOnlyList<MacroItem> _wheelMacros = [];
    private WheelMenuRules? _wheelRules;

    /// <summary>
    /// Where the pointer is while a wheel is open. The hook writes it and the frame timer reads it,
    /// so a pointer that moves a thousand times a second still costs one drawing a frame.
    /// </summary>
    private readonly WheelAim _wheelAim = new();
    private int _wheelAimScheduled;
    private long _wheelOpenedAt;
    private long _wheelMovesAtOpen;
    private long _wheelDrawnAtOpen;

    /// <summary>
    /// Counts wheels rather than events: an answer posted for one wheel that arrives after it has
    /// closed must not touch the next one, which is open at the same place on the screen.
    /// </summary>
    private int _wheelGeneration;

    /// <summary>Hands the hook thread a copy of the wheel's positions.</summary>
    private void RefreshWheelGate(MainViewModel viewModel)
        => Volatile.Write(ref _wheelGateRules, viewModel.Triggers.Wheel.Copy());

    /// <summary>
    /// Whether the trigger button belongs to the wheel at the place the press happened. It belongs
    /// there unless "only in these programs" is on, in which case a program scene has to match; the
    /// matched scene is kept so the wheel that opens is the one the press decided on.
    /// </summary>
    private bool WheelCanOpen()
    {
        _wheelPressScene = null;
        if (Volatile.Read(ref _systemRunning) != 1)
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
            _wheelPressScene = rules.SceneFor(
                _devices.Windows.Foreground(), _devices.Windows);
        }
        catch
        {
            _wheelPressScene = null;
        }

        return _wheelPressScene is not null;
    }

    // ── Wheel menu ────────────────────────────────────────────────────────────

    private void OnWheelStateChanged()
        => Volatile.Write(ref _wheelState, WheelMenuStore.Load());

    /// <summary>
    /// A press only starts the gesture. It never suppresses the event: a short right click must
    /// stay an ordinary right click so the system context menu still opens.
    /// </summary>
    private void OnWheelMouseDown(MouseHookEvent e)
    {
        try
        {
            // A click this program sent itself (the one a short press is answered with) must not
            // arm the gesture again.
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

    private void OnWheelMouseMoved(MouseMovedHookEvent e)
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

    /// <summary>
    /// The release belongs to the gesture that took the press. An open wheel answers with the
    /// sector that was aimed at; a press that never moved is the ordinary click it turned out to
    /// be, so the same button is sent again at that place. Neither reaches the application before
    /// this point, which is what keeps it from painting its own selection while the wheel is being
    /// aimed and from putting its context menu over the action that was chosen.
    /// </summary>
    private void OnWheelMouseUp(MouseHookEvent e)
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

    /// <summary>
    /// Sends the click the gesture took but never used. It goes out on a worker thread because the
    /// device layer waits between the press and the release, and a hook callback has to answer the
    /// system at once. The click is marked as this program's input, so it cannot rearm the wheel.
    /// </summary>
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
                // Where the pointer is now, rather than where it was released: sending the click
                // through a position would pull the cursor back if the hand had already moved on.
                var at = _devices.Input.Cursor;
                _devices.Input.Click(name, at.X, at.Y, 1, 0);
            }
            catch (Exception error)
            {
                AppDiagnostics.Record("wheel.replay", error);
            }
        });
    }

    private void OnWheelKeyDown(KeyHookEvent e)
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

    /// <summary>
    /// Scrolling while the wheel is open changes which scene its positions come from: up goes to
    /// the global scene, down to the current program's scene, and the same direction again returns
    /// to the merged view: "show me only this app's actions".
    /// </summary>
    private void OnWheelScroll(WheelHookEvent e)
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
                if (_wheelWindow is { } window)
                {
                    window.SetLayout(LayoutFor(Volatile.Read(ref _wheelState).Options));
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
                // Nothing is posted per move. The hook writes where the pointer is; one read is
                // scheduled for the next frame, and it draws whatever arrived by then.
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

    /// <summary>
    /// Hands one wheel answer to the interface. The hook thread must not touch a window, and an
    /// exception on the interface thread ends the process with nothing written down: a stage that
    /// fails closes the wheel and leaves a record instead. An answer that arrives after the wheel
    /// it belonged to has gone is dropped.
    /// </summary>
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
        if (_wheelWindow is not null || DataContext is not MainViewModel viewModel)
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
            // The scene is chosen once, from the window that was in front when the button went
            // down, exactly as the floating macro menu picks its lower region. The positions come
            // from the project, the button and the look from this machine's file. The copy taken
            // for the gate is used here as well: it is the wheel the press was judged against.
            _wheelMacros = [.. viewModel.Macros];
            _wheelRules = Volatile.Read(ref _wheelGateRules) ?? viewModel.Triggers.Wheel.Copy();
            _wheelSource = WheelSource.Merged;
            _wheelScene = _wheelPressScene;
            if (_wheelScene is null && !Volatile.Read(ref _wheelState).Options.OnlyInPrograms)
            {
                try
                {
                    _wheelScene = _wheelRules.SceneFor(
                        _devices.Windows.Foreground(),
                        _devices.Windows);
                }
                catch
                {
                    _wheelScene = null;
                }
            }

            var layout = LayoutFor(state.Options);
            var window = new WheelMenuWindow(layout, state.Options, origin);
            window.TimedOut += () =>
            {
                _wheel?.Timeout();
                CloseWheel();
            };
            window.ShowAsPeer(this);
            window.Position = window.PlaceAt(origin);
            _wheelWindow = window;
            Volatile.Write(ref _wheelHostOpen, 1);
            _wheel?.SetCenter(window.Center.X, window.Center.Y, window.Metrics);

            // Everything that belonged to the wheel before this one is stale, including a move
            // that was written but never drawn. The generation itself moves when a wheel closes,
            // not when one opens: an answer posted before the open ran still belongs to the wheel
            // that is now up, and dropping it would leave a release unanswered.
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

    /// <summary>
    /// Asks for one read of the aim at the next render. Only one read is ever waiting: a move that
    /// arrives while it is pending is covered by it, because the read takes the newest position.
    /// It runs with the render pass rather than on a timer, because a timer at input priority waits
    /// behind the drawing it is meant to keep up with — the guide line then trails the pointer by
    /// as many frames as the drawing took.
    /// </summary>
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
        if (_wheelWindow is null)
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
        if (_wheelWindow is not { } window)
        {
            return;
        }

        var dx = x - window.Center.X;
        var dy = y - window.Center.Y;
        var chosen = WheelGeometry.At(dx, dy, window.Metrics);
        _wheel?.Hover(chosen);
        window.Hover(chosen);
        window.Aim(x, y);
    }

    private void RunWheelSlot(WheelSlotRef? reference)
    {
        if (reference is not { } chosen
            || _wheelWindow?.SlotAt(chosen) is not { Macro: { } macro })
        {
            return;
        }

        _triggers?.RunOnce(macro);
    }

    private void CloseWheel()
    {
        var window = _wheelWindow;
        _wheelWindow = null;
        Volatile.Write(ref _wheelHostOpen, 0);
        _wheelScene = null;
        _wheelRules = null;
        _wheelMacros = [];
        _wheelSource = WheelSource.Merged;

        // Bumping the generation is what makes an answer still in the queue belong to a wheel that
        // no longer exists; a scheduled read stops with it.
        _wheelGeneration++;
        _wheelAim.Clear();
        NoteWheelFrames();
        window?.Close();
    }

    /// <summary>
    /// One line about what a wheel cost, written when it closes. The pair of numbers is the whole
    /// diagnosis of a pointer that felt slow: positions written against drawings made.
    /// </summary>
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
