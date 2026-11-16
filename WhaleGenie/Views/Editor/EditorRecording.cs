using System;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// What a recording does to the editor and the page it was started from: asks the layer for a
/// recorder, puts the window out of the way, keeps the captured count in the status line, and on
/// the way back hands the steps to that same page and restores the window.
/// </summary>
/// <remarks>
/// The session owns the recorder and the steps; what is left here is bookkeeping the window used to
/// hold — a timer, the page a recording belongs to, and the options the panel has set. The page is
/// remembered rather than read again when the recording ends: switching tabs does not move a
/// recording that is already running.
/// </remarks>
internal sealed class EditorRecording(
    Window host,
    MacroEditorSession session,
    Func<MacroEditorViewModel?> viewModel,
    Func<IDeviceLayer> devices)
{
    private DispatcherTimer? _timer;
    private MacroEditorViewModel? _recordingFor;

    /// <summary>
    /// Starts a recording from the page in front, or answers false when the layer would not give a
    /// recorder. The window is minimised only after the recorder is running.
    /// </summary>
    public bool Start()
    {
        if (viewModel() is not { } model)
        {
            return false;
        }

        if (!session.Recording.Start(devices(), model.RecordIgnoreInjected,
                model.RecordGamepadButtons, () => Dispatcher.UIThread.Post(() => model.StopRecording())))
        {
            return false;
        }

        _recordingFor = model;
        StartTicking(model);

        // Out of the way, so the work being recorded is not itself recorded as clicks on the
        // editor. The shortcuts that end a recording still reach it from anywhere.
        host.WindowState = WindowState.Minimized;
        return true;
    }

    /// <summary>
    /// Ends the recording and gives its steps to the page that started it. Null when nothing was
    /// recording; otherwise how many events were caught, which is what "nothing was recorded" is
    /// told from.
    /// </summary>
    public int? Stop()
    {
        _timer?.Stop();
        _timer = null;

        if (_recordingFor is not { } model || !session.Recording.IsRecording)
        {
            _recordingFor = null;
            return null;
        }

        var outcome = session.Recording.Stop(Choices(model));
        model.AppendSteps(outcome.Steps);

        if (host.WindowState == WindowState.Minimized)
        {
            host.WindowState = WindowState.Normal;
        }

        host.Activate();
        _recordingFor = null;
        return outcome.Captured;
    }

    /// <summary>The recording options as the panel on the editor has them set.</summary>
    internal static RecordingOptions Choices(MacroEditorViewModel model) => new()
    {
        MouseMovement = model.RecordMouseMovement,
        MouseButtons = model.RecordMouseButtons,
        KeyboardKeys = model.RecordKeyboardKeys,
        GamepadButtons = model.RecordGamepadButtons,
        Delays = model.RecordDelays,
        IgnoreDelaysBelowMs = (int)(model.IgnoreDelaysBelowMs ?? 0),
        RelativeMovement = model.PositionCapture == MousePositionMode.SavePositionDifferences,
    };

    /// <summary>Keeps the captured count in the status line without touching the hook thread.</summary>
    private void StartTicking(MacroEditorViewModel model)
    {
        _timer?.Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => model.ReportRecordedEvents(session.Recording.Captured);
        _timer.Start();
    }
}
