using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
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
                        Kind = ActionParameterKind.Image,
                        Value = @"C:\images\ok.png",
                    },
                    new StepParameter
                    {
                        Name = "confidence",
                        Kind = ActionParameterKind.Number,
                        Value = "90",
                    },
                    new StepParameter
                    {
                        Name = "resultVariable",
                        Kind = ActionParameterKind.Text,
                        Value = "match",
                    },
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
                    new StepParameter
                    {
                        Name = "width",
                        Kind = ActionParameterKind.Number,
                        Value = "20",
                    },
                    new StepParameter
                    {
                        Name = "height",
                        Kind = ActionParameterKind.Number,
                        Value = "20",
                    },
                    new StepParameter
                    {
                        Name = "saveTo",
                        Kind = ActionParameterKind.Text,
                        Value = $"shot{index}",
                    },
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
    /// A device layer with a screen that shows a picture and a search that always finds it, so the
    /// engine looks at something without a real screen being read. Everything else is refused the
    /// way a machine with no devices would refuse it.
    /// </summary>
    private sealed class LookingDevices : IDeviceLayer, IScreenDevice, IVisionDevice
    {
        public IScreenDevice Screen => this;

        public IVisionDevice Vision => this;

        public IInputRouter Inputs => NullDeviceLayer.Instance.Inputs;

        public IInputDevice Input => NullDeviceLayer.Instance.Input;

        public IGamepadDevice Gamepad => NullDeviceLayer.Instance.Gamepad;

        public IOcrDevice Ocr => NullDeviceLayer.Instance.Ocr;

        public IUiDevice Ui => NullDeviceLayer.Instance.Ui;

        public IFileDevice Files => NullDeviceLayer.Instance.Files;

        public IClipboardDevice Clipboard => NullDeviceLayer.Instance.Clipboard;

        public IProcessDevice Processes => NullDeviceLayer.Instance.Processes;

        public ISystemDevice System => NullDeviceLayer.Instance.System;

        public IWindowDevice Windows => NullDeviceLayer.Instance.Windows;

        public IBrowserDevice Browser => NullDeviceLayer.Instance.Browser;

        public ScreenSize PrimarySize => new(60, 40);

        public PixelColor PixelAt(int x, int y) => default;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ImageFrame? Load(string path) => new(4, 4, new byte[64]);

        public ImageMatch? Find(ImageFrame haystack, ImageFrame needle, double confidencePercent)
            => new(0.95, new ScreenPoint(10, 12), new ScreenSize(4, 4));

        public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
            double confidencePercent, int limit)
            => [new(0.95, new ScreenPoint(10, 12), new ScreenSize(4, 4))];
    }
}
