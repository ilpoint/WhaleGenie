using System.Collections.Generic;

using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.Platform.Windows;

namespace WhaleGenie.Platform.Tests;

/// <summary>
/// The recorder's own reading of a controller: what it was holding when the reading began is taken
/// up rather than written down, the way a key already down is.
/// </summary>
public class InputRecorderTests
{
    /// <summary>
    /// The recorder writes down the controller as well as the keyboard and the mouse. Windows reports
    /// no controller button, so the recorder reads it while a recording is going; what it was holding
    /// when the reading began is taken up rather than written down, the way a key already down is.
    /// </summary>
    [Fact]
    public void The_recorder_writes_down_the_controller_it_was_given()
    {
        var pad = new PlayedPad { Down = GamepadButtons.A };
        var recorder = new InputHookRecorder(pad);
        var captured = new List<RecordedInput>();
        recorder.Captured += captured.Add;
        recorder.TakeUpController();
        recorder.ReadButtons();
        Assert.Empty(captured);
        pad.Down = GamepadButtons.None;
        recorder.ReadButtons();
        pad.Down = GamepadButtons.LeftBumper;
        recorder.ReadButtons();
        Assert.Equal(2, captured.Count);
        Assert.Equal(RecordedInputKind.GamepadUp, captured[0].Kind);
        Assert.Equal("a", captured[0].Key);
        Assert.Equal(RecordedInputKind.GamepadDown, captured[1].Kind);
        Assert.Equal("lb", captured[1].Key);
        // The panel can leave the controller out of a recording altogether.
        recorder.RecordController = false;
        pad.Down = GamepadButtons.None;
        recorder.ReadButtons();
        Assert.Equal(2, captured.Count);
    }

    /// <summary>A controller a check plays: what is held is whatever the check last set.</summary>
    private sealed class PlayedPad : IGamepadReading
    {
        public bool Connected { get; set; } = true;

        public GamepadButtons Down { get; set; }

        public GamepadButtons Held() => Connected ? Down : GamepadButtons.None;
    }
}
