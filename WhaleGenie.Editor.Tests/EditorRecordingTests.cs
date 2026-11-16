using System;
using Avalonia.Controls;
using WhaleGenie.Core.Recording;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// What one recording does to the window around it: the window goes out of the way only once the
/// recorder is running, and comes back when it stops. The session owns the recorder; these checks
/// are about the bookkeeping the editor used to hold itself.
/// </summary>
public class EditorRecordingTests
{
    [Fact]
    public void A_recording_takes_the_window_out_of_the_way_and_brings_it_back()
    {
        Ui.Run(() =>
        {
            var (recording, session, window) = SetUp(new FakeRecorder());

            Assert.True(recording.Start());
            Assert.Equal(WindowState.Minimized, window.WindowState);
            Assert.True(session.Recording.IsRecording);

            Assert.Equal(0, recording.Stop());

            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.False(session.Recording.IsRecording);
            window.Close();
        });
    }

    [Fact]
    public void A_layer_that_will_not_record_leaves_the_window_where_it_was()
    {
        Ui.Run(() =>
        {
            // The refusal recorder answers nothing: the layer is a machine that cannot record, not
            // a window that should be minimised for a recording that never started.
            var (recording, session, window) = SetUp(null);

            Assert.False(recording.Start());

            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.False(session.Recording.IsRecording);
            window.Close();
        });
    }

    [Fact]
    public void The_options_are_read_from_the_page_that_starts_the_recording()
    {
        Ui.Run(() =>
        {
            var (_, _, window) = SetUp(new FakeRecorder());
            var model = new MacroEditorViewModel
            {
                Persisted = new PersistedVariables(),
                Variables = new VariableSession(),
                RecordMouseMovement = false,
                RecordMouseButtons = true,
                RecordKeyboardKeys = false,
                RecordGamepadButtons = true,
                RecordDelays = false,
                IgnoreDelaysBelowMs = 120,
                PositionCapture = MousePositionMode.SavePositionDifferences,
            };

            var choices = EditorRecording.Choices(model);

            Assert.False(choices.MouseMovement);
            Assert.True(choices.MouseButtons);
            Assert.False(choices.KeyboardKeys);
            Assert.True(choices.GamepadButtons);
            Assert.False(choices.Delays);
            Assert.Equal(120, choices.IgnoreDelaysBelowMs);
            Assert.True(choices.RelativeMovement);
            window.Close();
        });
    }

    private static (EditorRecording Recording, MacroEditorSession Session, Window Window) SetUp(
        IInputRecorder? recorder)
    {
        var macro = new MacroItem { Name = "recorded" };
        var session = new MacroEditorSession(macro, [macro], "assets");
        var root = new MacroEditorViewModel
        {
            Persisted = new PersistedVariables(),
            Variables = new VariableSession(),
        };
        root.LoadFrom(macro);

        var devices = new ChecksDevices { Recorder = recorder };
        var window = new Window();
        window.Show();
        return (new EditorRecording(window, session, () => root, () => devices), session, window);
    }

    /// <summary>A recorder that runs and catches nothing, which is what an empty recording is.</summary>
    private sealed class FakeRecorder : IInputRecorder
    {
        public bool IsRunning { get; private set; }

        public bool IgnoreInjected { get; set; }

        public bool RecordController { get; set; }

        public string StopShortcut { get; set; } = string.Empty;

        public event Action<RecordedInput>? Captured
        {
            add { }
            remove { }
        }

        public event Action? StopRequested
        {
            add { }
            remove { }
        }

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void Dispose()
        {
        }
    }
}
