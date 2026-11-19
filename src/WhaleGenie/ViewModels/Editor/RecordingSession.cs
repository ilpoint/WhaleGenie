using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One recording: the recorder the device layer handed out, what it has caught, and the steps the
/// catch turns into. It lived on the window as three fields and a lock, which made "is anything
/// being recorded" a question you could only answer by reading an event handler.
/// </summary>
/// <remarks>
/// What a recording does to the window around it — putting it out of the way, the captured count in
/// the status line, and handing the steps back to the page that started it — belongs to
/// <c>EditorRecording</c>. The window keeps what only it can do: telling the person when the layer
/// refused or when nothing was caught.
/// </remarks>
internal sealed class RecordingSession : IDisposable
{
    /// <summary>What one recording came to: the steps, and how many events they were made from.</summary>
    internal sealed record Outcome(IReadOnlyList<MacroStep> Steps, int Captured);

    private readonly List<RecordedInput> _captured = [];
    private readonly object _gate = new();
    private IInputRecorder? _recorder;
    private Action? _stopRequested;

    /// <summary>Whether a recording is running, which is what the panel's button shows.</summary>
    public bool IsRecording => _recorder is not null;

    /// <summary>How many events have been caught, read from the interface thread while the hook adds.</summary>
    public int Captured
    {
        get
        {
            lock (_gate)
            {
                return _captured.Count;
            }
        }
    }

    /// <summary>
    /// Starts recording through the layer handed in, so a check records what it plays into that
    /// layer rather than what a real keyboard is doing. False when the layer would not give a
    /// recorder, or the hook would not start.
    /// </summary>
    public bool Start(IDeviceLayer devices, bool ignoreInjected, bool recordController,
        Action stopRequested)
    {
        if (_recorder is not null)
        {
            return true;
        }

        lock (_gate)
        {
            _captured.Clear();
        }

        var recorder = devices.CreateRecorder();

        // Everything up to and including Start is a question the layer may refuse: a machine with
        // no recorder answers by throwing rather than by handing back something that does nothing,
        // and it may throw on the first subscription just as well as on Start. The answer is the
        // same either way — this recording did not begin — and nothing is left subscribed.
        var subscribed = false;
        try
        {
            recorder.Captured += OnCaptured;
            recorder.StopRequested += stopRequested;
            subscribed = true;
            recorder.IgnoreInjected = ignoreInjected;
            recorder.RecordController = recordController;

            // 停止键由这一次录制开始时从设置里取：引擎不读设置文件，而用户换成别的键应当立刻生效。
            recorder.StopShortcut = LocalSettings.LoadRecordStopKey();
            recorder.Start();
        }
        catch (Exception)
        {
            if (subscribed)
            {
                try
                {
                    recorder.Captured -= OnCaptured;
                    recorder.StopRequested -= stopRequested;
                }
                catch (Exception)
                {
                    // The same refusal that ended the subscription can end the clean-up; the
                    // recorder is being let go either way.
                }
            }

            try
            {
                recorder.Dispose();
            }
            catch (Exception)
            {
                // A refusal can refuse to be let go as well; there is nothing left to clean up.
            }

            return false;
        }

        _recorder = recorder;
        _stopRequested = stopRequested;
        return true;
    }

    /// <summary>
    /// Stops and answers with what was caught, translated the way the panel currently has it set.
    /// An empty answer is a real one: input another program sends is left out on purpose, so a
    /// recording over a remote session comes back with nothing.
    /// </summary>
    public Outcome Stop(RecordingOptions options)
    {
        var recorder = _recorder;
        var stopRequested = _stopRequested;
        _recorder = null;
        _stopRequested = null;

        if (recorder is null)
        {
            return new Outcome([], 0);
        }

        recorder.Captured -= OnCaptured;
        if (stopRequested is not null)
        {
            recorder.StopRequested -= stopRequested;
        }

        recorder.Stop();
        recorder.Dispose();

        List<RecordedInput> captured;
        lock (_gate)
        {
            captured = [.. _captured];
            _captured.Clear();
        }

        IReadOnlyList<MacroStep> steps = captured.Count == 0
            ? []
            : RecordingTranslator.Translate(captured, options).ToSteps();

        return new Outcome(steps, captured.Count);
    }

    /// <summary>Lets go of the hook without asking anything, which is what closing the window means.</summary>
    public void Dispose()
    {
        var recorder = _recorder;
        _recorder = null;
        _stopRequested = null;
        recorder?.Dispose();
    }

    private void OnCaptured(RecordedInput input)
    {
        lock (_gate)
        {
            _captured.Add(input);
        }
    }
}
