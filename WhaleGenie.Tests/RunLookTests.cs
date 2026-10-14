using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The run window's own record of what each step looked at: a picture on the step's row, which is
/// what a log line cannot say — a line says a picture was found, and this says what was on screen
/// when that was decided.
/// </summary>
public class RunLookTests
{
    /// <summary>
    /// A step told to hand one result over, written the way the file writes it: a row of the
    /// <c>returns</c> parameter with the key of the result and the name it goes by.
    /// </summary>
    private static StepParameter Handed(string key, string name) => new()
    {
        Name = "returns",
        Kind = ActionParameterKind.Returns,
        Rows =
        [
            new StepParameterRow
            {
                Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["key"] = key,
                    ["name"] = name,
                },
            },
        ],
    };

    /// <summary>One of the four numbers a capture step works on, written the way the file writes it.</summary>
    private static StepParameter Number(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Number, Value = value };

    [Fact]
    public void A_step_that_looked_at_the_screen_gets_a_picture_on_its_row()
    {
        var (has, thumbnail, other, look) = Ui.RunAsync(async () =>
        {
            var find = new MacroStep
            {
                Type = "vision.findImage",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "image",
                        Kind = ActionParameterKind.Images,
                        Rows = [StepParameterRow.Of("image", @"C:\images\ok.png")],
                    },
                    new StepParameter
                    {
                        Name = "confidence",
                        Kind = ActionParameterKind.Number,
                        Value = "90",
                    },
                    Handed("match", "match"),
                ],
            };

            var other = new MacroStep { Type = "control.delay" };
            StepIds.Settle([find, other]);

            var viewModel = new RunViewModel([find, other], new LookingDevices());
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            var row = viewModel.Steps.First(step => step.Id == find.Id);
            var idle = viewModel.Steps.First(step => step.Id == other.Id);
            return (row.HasLook, row.Thumbnail, idle.HasLook, viewModel.LookOf(find.Id));
        });

        Assert.True(has);
        Assert.NotNull(thumbnail);
        Assert.False(other);

        Assert.NotNull(look);
        Assert.Equal(LookRole.Hit, look!.Boxes[1].Role);
    }

    [Fact]
    public void A_run_that_keeps_looking_does_not_hold_every_picture_it_ever_took()
    {
        var (kept, oldest, newest) = Ui.RunAsync(async () =>
        {
            // Forty steps, each taking a picture: more than the window is willing to hold.
            var steps = Enumerable.Range(0, 40).Select(index => new MacroStep
            {
                Type = "vision.capture",
                Parameters =
                [
                    Number("x", "0"),
                    Number("y", "0"),
                    Number("width", "20"),
                    Number("height", "20"),
                    Handed("image", $"shot{index}"),
                ],
            }).ToList();

            StepIds.Settle(steps);

            var viewModel = new RunViewModel(steps, new LookingDevices());
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            var held = viewModel.Steps.Count(step => step.HasLook);
            return (held, viewModel.LookOf(steps[0].Id), viewModel.LookOf(steps[^1].Id));
        });

        Assert.Equal(30, kept);
        Assert.Null(oldest);
        Assert.NotNull(newest);
    }

    /// <summary>
    /// Which window a run is reading, said while it reads it. A step's own picture says what was
    /// there and not where the engine looked, and that is the answer when a step read the wrong
    /// window: the frame lands on the window it really read.
    /// </summary>
    [Fact]
    public void The_window_a_run_is_reading_is_reported_while_it_reads_it()
    {
        var (place, last) = Ui.RunAsync(async () =>
        {
            var desktop = new WindowsOnDesk(new WindowInfo(4242, "Notepad - notes.txt",
                new ScreenPoint(1000, 500), new ScreenSize(800, 600), false, false));
            var devices = new LookingDevices { Windows = desktop };

            var step = new MacroStep
            {
                Type = "vision.captureWindow",
                // The frame is put up from a message to the interface thread, so the run has to
                // still be going when that message lands: a step that ends the instant it started
                // lets the run finish first, and then there is no run left to frame a window for.
                Meta = new StepMeta { DelayAfterMs = 200 },
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "title",
                        Kind = ActionParameterKind.Text,
                        Value = "Notepad",
                    },
                    Handed("image", "shot"),
                ],
            };

            StepIds.Settle([step]);

            var frames = new List<FramePlace?>();
            var viewModel = new RunViewModel([step], devices);
            viewModel.WindowRead += read => frames.Add(read);
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            return (frames.FirstOrDefault(read => read is not null), frames[^1]);
        });

        // The window the step read is framed where it sits...
        Assert.NotNull(place);
        Assert.Equal(4242, place!.Value.Handle);
        Assert.Equal(new ScreenPoint(1000, 500), place.Value.Location);
        Assert.Equal(new ScreenSize(800, 600), place.Value.Size);

        // ...and the run ending takes the frame down again.
        Assert.Null(last);
    }

    /// <summary>
    /// A frame follows a window that is being moved while it is being read. The reading says where the
    /// window is for that look rather than where it was when the step started, which is what makes the
    /// frame worth watching during a wait: a window dragged while a macro waits on it is framed where
    /// it has been dragged to.
    /// </summary>
    [Fact]
    public void The_frame_follows_a_window_that_is_moved_while_it_is_read()
    {
        var places = Ui.RunAsync(async () =>
        {
            var devices = new LookingDevices { Windows = new WalkingWindow() };
            // Each look is followed by a pause, because the frame is put up from a message to the
            // interface thread: a step that ends the instant it started lets the run finish before
            // that message lands, and a frame for a run that is over is not put up at all.
            var steps = new[] { WindowCapture(delayAfterMs: 150), WindowCapture(delayAfterMs: 150) };
            StepIds.Settle(steps);

            var frames = new List<FramePlace?>();
            var viewModel = new RunViewModel(steps, devices);
            viewModel.WindowRead += read => frames.Add(read);
            viewModel.RunCommand.Execute(null);
            while (viewModel.IsBusy)
            {
                await Task.Delay(10);
            }

            return frames.Where(read => read is not null).Select(read => read!.Value).ToList();
        });

        Assert.True(places.Count >= 2, "each look said where it was reading");
        Assert.True(places[^1].Location.X > places[0].Location.X,
            $"the frame stayed at {places[0].Location.X} while the window went to "
            + $"{places[^1].Location.X}");
    }

    /// <summary>
    /// One step that reads the window named "Notepad" and keeps what it saw. A pause after it is
    /// for the checks that watch a frame go up: the frame arrives on the interface thread, and the
    /// run has to still be going when it does.
    /// </summary>
    private static MacroStep WindowCapture(int delayAfterMs = 0) => new()
    {
        Type = "vision.captureWindow",
        Meta = new StepMeta { DelayAfterMs = delayAfterMs },
        Parameters =
        [
            new StepParameter
            {
                Name = "title",
                Kind = ActionParameterKind.Text,
                Value = "Notepad",
            },
            Handed("image", "shot"),
        ],
    };

    /// <summary>
    /// A desktop with one window on it that is being carried across the screen: every look finds it
    /// somewhere else, the way a look finds a window that is being dragged.
    /// </summary>
    private sealed class WalkingWindow : IWindowDevice
    {
        private int _steps;

        private WindowInfo Here() => new(4242, "Notepad - notes.txt",
            new ScreenPoint(1000 + (_steps++ * 100), 500), new ScreenSize(800, 600), false, false);

        public IReadOnlyList<WindowInfo> List() => [Here()];

        public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match, WindowCompare compare,
            bool everyWhenEmpty = false)
            => [Here()];

        public WindowInfo? Find(string value, WindowMatch match, WindowCompare compare)
            => Filter(value, match, compare).FirstOrDefault();

        public string ProcessOf(long handle) => "notepad";

        public string ClassOf(long handle) => "Notepad";

        public bool Activate(long handle) => true;

        public bool Minimize(long handle) => true;

        public bool Maximize(long handle) => true;

        public bool Restore(long handle) => true;

        public bool Close(long handle) => true;

        public bool Move(long handle, int x, int y, int width, int height) => true;

        public ScreenPoint ClientOrigin(long handle) => new(0, 0);

        public ScreenSize ClientSize(long handle) => new(0, 0);
    }

    /// <summary>
    /// A device layer with a screen that shows a picture and a search that always finds it, so the
    /// engine looks at something without a real screen being read. Everything else is refused the
    /// way a machine with no devices would refuse it.
    /// </summary>
    private sealed class LookingDevices : IDeviceLayer, IScreenDevice, IVisionDevice
    {
        public IScreenDevice Screen => this;

        /// <summary>Nowhere, which is what a check that is about looking at pixels needs.</summary>
        public ScreenPoint Cursor => default;

        public IInputHook Hook => NullDeviceLayer.Instance.Hook;

        public IInputRecorder CreateRecorder() => NullDeviceLayer.Instance.CreateRecorder();

        public IWindowEvents WindowEvents { get; } = new NoWindowEvents();

        public IDriverInput Driver => NullDeviceLayer.Instance.Driver;

        public IVisionDevice Vision => this;

        public IInputRouter Inputs => NullDeviceLayer.Instance.Inputs;

        public IInputDevice Input => NullDeviceLayer.Instance.Input;

        public IGamepadDevice Gamepad => NullDeviceLayer.Instance.Gamepad;

        /// <summary>Automatic, like a machine nobody has set up to read the screen another way.</summary>
        public CaptureChoice Ways { get; } = new();

        ICaptureChoice IDeviceLayer.Capture => Ways;

        public IOcrDevice Ocr => NullDeviceLayer.Instance.Ocr;

        public IUiDevice Ui => NullDeviceLayer.Instance.Ui;

        public IFileDevice Files => NullDeviceLayer.Instance.Files;

        public IClipboardDevice Clipboard => NullDeviceLayer.Instance.Clipboard;

        public IProcessDevice Processes => NullDeviceLayer.Instance.Processes;

        public ISystemDevice System => NullDeviceLayer.Instance.System;

        /// <summary>The windows this machine has open. None by default, so a check that reads the
        /// screen for its own sake does not have to know about windows.</summary>
        public IWindowDevice Windows { get; set; } = NullDeviceLayer.Instance.Windows;

        public IBrowserDevice Browser => NullDeviceLayer.Instance.Browser;

        public ScreenLayout Desktop => new(new ScreenArea(0, 0, 60, 40),
            [new ScreenDisplay(new ScreenArea(0, 0, 60, 40), true)]);

        public PixelColor PixelAt(int x, int y) => default;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => new(new ImageFrame(request.Width, request.Height,
                new byte[request.Width * request.Height * 4]), new ScreenPoint(request.X, request.Y),
                request.Method != CaptureWay.Auto
                    ? request.Method
                    : request.Window == 0 ? CaptureWay.Gdi : CaptureWay.GraphicsCapture);

        public ImageFrame? Load(string path) => new(4, 4, new byte[64]);

        public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle, VisionQuery query)
            => [new(0.95, new ScreenPoint(10, 12), new ScreenSize(4, 4))];
    }

    /// <summary>
    /// The windows a check puts on the pretend desktop. Only what a reading of a window needs is
    /// answered for real — which windows there are and which of them a name picks — and everything
    /// else is the flat "yes" a window that is never acted on deserves.
    /// </summary>
    private sealed class WindowsOnDesk(params WindowInfo[] windows) : IWindowDevice
    {
        public IReadOnlyList<WindowInfo> List() => [.. windows];

        public IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match, WindowCompare compare,
            bool everyWhenEmpty = false)
            => [.. windows.Where(window => window.Title.Contains(value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase))];

        public WindowInfo? Find(string value, WindowMatch match, WindowCompare compare)
            => Filter(value, match, compare).FirstOrDefault();

        public string ProcessOf(long handle) => string.Empty;

        public string ClassOf(long handle) => string.Empty;

        public bool Activate(long handle) => true;

        public bool Minimize(long handle) => true;

        public bool Maximize(long handle) => true;

        public bool Restore(long handle) => true;

        public bool Close(long handle) => true;

        public bool Move(long handle, int x, int y, int width, int height) => true;

        public ScreenPoint ClientOrigin(long handle) => new(0, 0);

        public ScreenSize ClientSize(long handle) => new(0, 0);
    }
}
