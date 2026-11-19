using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The run button under the form a step is written in. It runs the step the way it is written — a
/// click is a click rather than a look — and hands what the step saw to the picture window once it
/// is done, which is still where "did it hit the right one" is answered.
/// </summary>
public class TrialRunTests
{
    /// <summary>
    /// 点图片这一步从表单里试跑，必须真的点下去：只识别、不点击会把「试」变成「看」，而用户按下的是
    /// 运行。点的位置仍旧是识别到的那块画面的中心，跑完照样把看到的画面摆出来。
    /// </summary>
    [Fact]
    public void A_picture_click_from_the_form_clicks_what_it_found()
    {
        Ui.RunAsync(async () =>
        {
            var window = Open("vision.clickImage", out var form);

            var mouse = new ClickingMouse();
            var machine = Machine(mouse, new ImageMatch(0.95, new ScreenPoint(20, 10),
                new ScreenSize(4, 4)));
            window.Devices = machine;

            RunButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            // The step is run off the thread that draws the window, so the click arrives a moment
            // after the button is pressed.
            await Ui.Until(() => mouse.Clicks.Count > 0, "the run never reached the mouse");

            // The middle of the picture it found, which is where a person would click it.
            Assert.Equal("click left 22 12 1 0", mouse.Clicks[0]);

            // What it saw is still shown: the picture a click that landed on the wrong thing is
            // read off is the one the look used to put up.
            await Ui.Until(() => window.OwnedWindows.OfType<LookWindow>().Any(),
                "what the step saw was not shown after it ran");

            window.Close();
            return true;
        });
    }

    /// <summary>
    /// 什么都没找到时，不点，但看见的画面照样摆出来 —— 那才是"为什么没点上"的答案。
    /// </summary>
    [Fact]
    public void A_click_that_found_nothing_clicked_nothing_and_still_showed_the_screen()
    {
        Ui.RunAsync(async () =>
        {
            var window = Open("vision.clickImage", out var form);

            // A tenth of the default ten seconds: what is being checked is that a trial ends where
            // the step's own clock ends, not how long a wait takes.
            form.MetaTimeout.Value = 600;
            Dispatcher.UIThread.RunJobs();

            var mouse = new ClickingMouse();
            window.Devices = Machine(mouse);

            RunButton(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            await Ui.Until(() => window.OwnedWindows.OfType<LookWindow>().Any(),
                "what the step saw was not shown when it found nothing");
            Assert.Empty(mouse.Clicks);

            // The step failed, so the form says why: the message is answered here rather than left
            // standing in a modal loop the next check would inherit.
            await Ui.Until(() => window.OwnedWindows.OfType<ConfirmDialog>().Any(),
                "the failed step was not reported");
            ((ConfirmDialogViewModel)window.OwnedWindows.OfType<ConfirmDialog>().Single().DataContext!)
                .PrimaryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            window.Close();
            return true;
        });
    }

    /// <summary>The form standing on a picture click with a picture named, ready to be run.</summary>
    private static ActionEditorWindow Open(string action, out AddActionViewModel form)
    {
        var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], []);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        form = (AddActionViewModel)window.DataContext!;
        form.SelectAction(action);
        Dispatcher.UIThread.RunJobs();

        form.Parameters.First(parameter => parameter.Definition.Name == "images")
            .AddPicture(new ImageRowViewModel { Text = @"C:imagesok.png" });
        Dispatcher.UIThread.RunJobs();
        Assert.True(form.CanRun, form.ValidationMessage);
        return window;
    }

    /// <summary>
    /// 一台会找图的机器，鼠标被换成记账的那一只：识别结果由检查摆好，别的一概像个没有设备的机器。
    /// </summary>
    private static ChecksDevices Machine(ClickingMouse mouse, params ImageMatch[] found)
        => new()
        {
            Vision = new VisionForChecks(found),
            Screen = new ScreenForChecks(),
            Inputs = new SingleInputRouter(mouse),
        };

    /// <summary>The run button under the form, found the way a user finds it: by what it says.</summary>
    private static Button RunButton(Window window)
        => window.GetVisualDescendants().OfType<Button>()
            .First(item => item.IsEffectivelyVisible
                && Equals(item.Content, Strings.Get("Add.RunStep")));

    /// <summary>
    /// 记账的鼠标：只有"点了哪里"这一件事被回答，其余一概拒绝 —— 这一次试跑该发的只有一次点击，
    /// 发出别的东西说明这一步已经不是它写的样子了。
    /// </summary>
    private sealed class ClickingMouse : IInputDevice
    {
        /// <summary>Every click sent, written the way the device was asked for it.</summary>
        public List<string> Clicks { get; } = [];

        public ScreenPoint Cursor => default;

        public void KeyPress(string key, int holdMs) => throw Unspent();

        public void KeyDown(string key) => throw Unspent();

        public void KeyUp(string key) => throw Unspent();

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => throw Unspent();

        public void TypeText(string text, int intervalMs) => throw Unspent();

        public void MoveMouse(int x, int y, int durationMs)
        {
        }

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
        {
        }

        public void MoveMouseRelative(int dx, int dy, int durationMs) => throw Unspent();

        public void MouseDown(string button, int x, int y) => throw Unspent();

        public void MouseUp(string button, int x, int y) => throw Unspent();

        public void Click(string button, int x, int y, int clicks, int intervalMs)
            => Clicks.Add($"click {button} {x} {y} {clicks} {intervalMs}");

        public void Scroll(string direction, int delta, int x, int y) => throw Unspent();

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs,
            int steps) => throw Unspent();

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => throw Unspent();

        private static Exception Unspent()
            => new InvalidOperationException(
                "the trial sent something other than the click it was written for");
    }

    /// <summary>A picture search that answers with what the check put there, wherever it looks.</summary>
    private sealed class VisionForChecks(params ImageMatch[] found) : IVisionDevice
    {
        public ImageFrame? Load(string path) => new(4, 4, new byte[64]);

        public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
            VisionQuery query) => found;
    }

    /// <summary>A screen with a picture on it wide enough to search, and a picture of it to hand.</summary>
    private sealed class ScreenForChecks : IScreenDevice
    {
        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 120, 40),
            [new ScreenDisplay(new ScreenArea(0, 0, 120, 40), true)]);

        public ScreenPoint Cursor => default;

        public PixelColor PixelAt(int x, int y) => default;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => new(new ImageFrame(request.Width, request.Height,
                    new byte[request.Width * request.Height * 4]), new ScreenPoint(request.X, request.Y),
                CaptureWay.Gdi);

        public ImageFrame? Load(string path) => new(4, 4, new byte[64]);
    }
}
