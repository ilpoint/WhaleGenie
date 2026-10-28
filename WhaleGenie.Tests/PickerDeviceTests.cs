using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The pickers and the recorder work over the device layer they are handed rather than over this
/// machine. That is the whole point of the layer: the same windows run on a stand-in screen, a
/// stand-in control reader and a stand-in recorder, so a second implementation of any of them costs
/// the windows nothing — which is what these checks are here to keep true.
/// </summary>
public class PickerDeviceTests
{
    /// <summary>The control an element pick is meant to find.</summary>
    private static readonly UiElementInfo Save = new("Save", "saveButton", "Button", "ButtonClass",
        new ScreenPoint(1000, 500), new ScreenSize(80, 24), "Untitled - Notepad");

    [Fact]
    public void The_colour_magnifier_reads_the_screen_it_was_given()
    {
        Ui.Run(() =>
        {
            var machine = new Machine
            {
                Colour = new PixelColor(0x12, 0x34, 0x56),
                Cursor = new ScreenPoint(120, 240),
            };

            var window = new ColorPickerWindow(machine, machine, NullDeviceLayer.Instance.Hook);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new PixelColor(0x12, 0x34, 0x56), window.Colour);

            // The arrow keys walk the pointer a pixel at a time through the layer's own input
            // device, which is how a colour is taken from a spot a mouse cannot be settled on.
            window.Nudge(Key.Left, 1);

            Assert.Equal(["moveMouse 119,240"], machine.Sent);
            window.Close();
        });
    }

    [Fact]
    public void The_element_picker_reads_the_control_the_layer_points_at()
    {
        Ui.Run(() =>
        {
            var machine = new Machine { Cursor = new ScreenPoint(1000, 500), Element = Save };

            var window = new ElementPickerWindow(new ChecksDevices { Screen = machine, Ui = machine });
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // Opening the picker is already enough: it follows the pointer it was given.
            Assert.Equal("saveButton", window.Hovered?.AutomationId);
            Assert.Equal(["elementAt 1000,500"], machine.Sent);

            window.Confirm();

            Assert.Equal(
                new ElementPickerWindow.Pick("Button[automationId='saveButton']", "Untitled - Notepad"),
                window.Chosen);
        });
    }

    [Fact]
    public void Recording_goes_through_the_recorder_the_layer_hands_out()
    {
        Ui.Run(() =>
        {
            var recorder = new Recorder();
            var devices = new ChecksDevices { Recorder = recorder };
            var macro = new MacroItem { Name = "录制的" };

            var window = new MacroEditorWindow(macro, [macro], null, devices);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = Assert.IsType<MacroEditorViewModel>(window.DataContext);
            viewModel.ToggleRecordCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(recorder.IsRunning);
            Assert.Equal(viewModel.RecordIgnoreInjected, recorder.IgnoreInjected);

            // What the recorder catches becomes steps, exactly as it would from a real keyboard.
            recorder.Play(RecordedInput.KeyDown("F6", 0));
            recorder.Play(RecordedInput.KeyUp("F6", 120));

            viewModel.StopRecording();
            Dispatcher.UIThread.RunJobs();

            Assert.False(recorder.IsRunning);
            Assert.True(recorder.Disposed);

            var step = Assert.Single(viewModel.Steps, step => step.Type == "input.keyPress");
            Assert.Equal("F6", step.Parameters.Single(parameter => parameter.Name == "key").Value);

            window.Close();
        });
    }

    /// <summary>
    /// A controller button is recorded too, and becomes the step that presses the virtual one. Windows
    /// reports no controller button, so what the recorder hands over comes from reading the controller;
    /// from there a recording is the same as any other — a quick press becomes one tap, and the panel's
    /// controller switch takes the controller out of the recording altogether.
    /// </summary>
    [Fact]
    public void A_recorded_controller_button_becomes_a_step()
    {
        Ui.Run(() =>
        {
            var recorder = new Recorder();
            var devices = new ChecksDevices { Recorder = recorder };
            var macro = new MacroItem { Name = "录手柄的" };

            var window = new MacroEditorWindow(macro, [macro], null, devices);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = Assert.IsType<MacroEditorViewModel>(window.DataContext);
            viewModel.ToggleRecordCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(recorder.RecordController, "the panel did not ask for the controller");

            recorder.Play(RecordedInput.GamepadDown("a", 0));
            recorder.Play(RecordedInput.GamepadUp("a", 120));

            viewModel.StopRecording();
            Dispatcher.UIThread.RunJobs();

            var step = Assert.Single(viewModel.Steps, step => step.Type == "gamepad.button");
            Assert.Equal("a", step.Parameters.Single(parameter => parameter.Name == "button").Value);
            Assert.Equal("tap", step.Parameters.Single(parameter => parameter.Name == "mode").Value);
            Assert.Equal("120", step.Parameters.Single(parameter => parameter.Name == "holdMs").Value);

            // With the switch off the recorder is told so, and a button pressed while recording is
            // not written down at all.
            viewModel.RecordGamepadButtons = false;
            viewModel.ToggleRecordCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(recorder.RecordController, "the panel's switch did not reach the recorder");

            recorder.Play(RecordedInput.GamepadDown("b", 0));
            recorder.Play(RecordedInput.GamepadUp("b", 60));

            viewModel.StopRecording();
            Dispatcher.UIThread.RunJobs();

            Assert.Single(viewModel.Steps, step => step.Type == "gamepad.button");
        });
    }

    /// <summary>
    /// A machine that answers with what a check put in it: one colour everywhere, the pointer at one
    /// place, one control under it, and every way of sending input written down rather than sent.
    /// Everything that is not one of those is refused the way a machine with no devices refuses it.
    /// </summary>
    private sealed class Machine
        : IScreenDevice, WhaleGenie.Core.Devices.IInputDevice, IUiDevice, IInputHook
    {
        /// <summary>What the input device was asked to do, in the order it was asked.</summary>
        public List<string> Sent { get; } = [];

        public PixelColor Colour { get; set; }

        public ScreenPoint Cursor { get; set; }

        public UiElementInfo? Element { get; set; }

        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        public PixelColor PixelAt(int x, int y) => Colour;

        public ImageFrame Capture(int x, int y, int width, int height)
            => NullDeviceLayer.Instance.Screen.Capture(x, y, width, height);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => NullDeviceLayer.Instance.Screen.Capture(request);

        public UiElementInfo? ElementAt(int x, int y)
        {
            Sent.Add($"elementAt {x},{y}");
            return Element;
        }

        public void MoveMouse(int x, int y, int durationMs) => Sent.Add($"moveMouse {x},{y}");

        public void KeyPress(string key, int holdMs) => Refused(key);

        public void KeyDown(string key) => Refused(key);

        public void KeyUp(string key) => Refused(key);

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => Refused("hotkey");

        public void TypeText(string text, int intervalMs) => Refused("typeText");

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs) => Refused("moveMouseAlong");

        public void MoveMouseRelative(int dx, int dy, int durationMs) => Refused("moveMouseRelative");

        public void MouseDown(string button, int x, int y) => Refused("mouseDown");

        public void MouseUp(string button, int x, int y) => Refused("mouseUp");

        public void Click(string button, int x, int y, int clicks, int intervalMs) => Refused("click");

        public void Scroll(string direction, int delta, int x, int y) => Refused("scroll");

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs,
            int steps) => Refused("drag");

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => Refused("dragAlong");

        public bool Exists(UiQuery query, int timeoutMs) => Refused<bool>("exists");

        public bool Click(UiQuery query, string button) => Refused<bool>("clickElement");

        public bool Invoke(UiQuery query) => Refused<bool>("invoke");

        public UiElementFacts? Facts(UiQuery query) => Refused<UiElementFacts?>("facts");

        public bool FocusWindow(UiQuery query) => Refused<bool>("focus");

        public bool SetText(UiQuery query, string text, bool clearFirst) => Refused<bool>("setText");

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
            => Refused<IReadOnlyList<UiElementInfo>>("findAll");

        public bool Select(UiQuery query, string text, int itemIndex) => Refused<bool>("select");

        public bool SetChecked(UiQuery query, bool? state) => Refused<bool>("setChecked");

        public bool SetExpanded(UiQuery query, string action) => Refused<bool>("setExpanded");

        public bool ScrollIntoView(UiQuery query) => Refused<bool>("scrollIntoView");

        public UiTable ReadTable(UiQuery query, int limit) => Refused<UiTable>("readTable");

        // Nothing is ever pressed on this machine's keyboard, so the seven events are never raised.
        // The four that share a name with a way of sending input are named through the interface.
        event Action<KeyHookEvent>? IInputHook.KeyDown
        {
            add { }
            remove { }
        }

        event Action<KeyHookEvent>? IInputHook.KeyUp
        {
            add { }
            remove { }
        }

        event Action<MouseHookEvent>? IInputHook.MouseDown
        {
            add { }
            remove { }
        }

        event Action<MouseHookEvent>? IInputHook.MouseUp
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedHookEvent>? Moved
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedRelativeHookEvent>? MovedRelative
        {
            add { }
            remove { }
        }

        public event Action<WheelHookEvent>? Wheel
        {
            add { }
            remove { }
        }

        private static void Refused(string what) => throw new DeviceUnavailableException(what);

        private static T Refused<T>(string what)
        {
            Refused(what);
            return default!;
        }
    }

    /// <summary>
    /// A recorder that hands the events on rather than listening to anything, so that what the
    /// editor does with a recording can be read without a keyboard being touched.
    /// </summary>
    private sealed class Recorder : IInputRecorder
    {
        public bool IsRunning { get; private set; }

        public bool IgnoreInjected { get; set; }

        /// <summary>What the editor asked for, so the panel's controller switch can be read back.</summary>
        public bool RecordController { get; set; }

        public bool Disposed { get; private set; }

        public event Action<RecordedInput>? Captured;

        public event Action? StopRequested
        {
            add { }
            remove { }
        }

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        public void Dispose() => Disposed = true;

        /// <summary>One event, the way the hook would have reported it.</summary>
        public void Play(RecordedInput input) => Captured?.Invoke(input);
    }
}
