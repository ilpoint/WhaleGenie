using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// A closed target, one overlay at a time, and the frame a target keeps.
public partial class WindowRulePickerTests
{
    [Fact]
    public void A_point_picker_is_one_overlay_and_hides_its_crosshair_outside_the_target()
    {
        Ui.Run(() =>
        {
            var hook = new KeyHook();
            var screen = new ScreenAt(new ScreenPoint(5, 5));
            var windows = new WindowsForChecks().Add(9, "Game", "game", "GameClass")
                .ClientAt(9, 10, 20);
            var devices = new ChecksDevices { Screen = screen, Windows = windows, Hook = hook };
            var picker = new PointPickerWindow(devices.Screen, devices.Windows, devices.Ui, devices.Hook, new WindowRule(Title: "Game"));
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.DoesNotContain(typeof(PointPickerWindow)
                    .GetFields(System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic),
                    field => typeof(Window).IsAssignableFrom(field.FieldType));
                Assert.False(picker.GetVisualDescendants().OfType<Canvas>()
                    .First(control => control.Name == "Crosshair").IsVisible);
                Assert.Contains("F1", picker.GetVisualDescendants().OfType<TextBlock>()
                    .First(control => control.Name == "ReadoutText").Text,
                    StringComparison.Ordinal);

                var f1 = KeyNames.Resolve("F1")!.Value;
                Assert.True(hook.Press(f1, down: true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(hook.Press(f1, down: false));
                Dispatcher.UIThread.RunJobs();

                Assert.Null(picker.Chosen);
                Assert.True(picker.IsVisible);

                var escape = KeyNames.Resolve("Esc")!.Value;
                hook.Press(escape, down: true);
                hook.Press(escape, down: false);
                Dispatcher.UIThread.RunJobs();
                Assert.False(picker.IsVisible);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    [Fact]
    public void A_closed_target_is_named_in_the_readout_and_only_escape_ends_picking()
    {
        Ui.Run(() =>
        {
            var hook = new KeyHook();
            var screen = new ScreenAt(new ScreenPoint(50, 60));
            var windows = new WindowsForChecks().Add(9, "Game", "game", "GameClass")
                .ClientAt(9, 10, 20);
            var devices = new ChecksDevices { Screen = screen, Windows = windows, Hook = hook };
            var picker = new PointPickerWindow(devices.Screen, devices.Windows, devices.Ui, devices.Hook, new WindowRule(Title: "Game"));
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();
                windows.Open.Clear();
                typeof(PointPickerWindow).GetMethod("Follow",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)!.Invoke(picker, null);
                Dispatcher.UIThread.RunJobs();

                var readout = picker.GetVisualDescendants().OfType<TextBlock>()
                    .First(control => control.Name == "ReadoutText");
                Assert.Contains("目标窗口已关闭", readout.Text, StringComparison.Ordinal);

                var f1 = KeyNames.Resolve("F1")!.Value;
                Assert.True(hook.Press(f1, down: true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(hook.Press(f1, down: false));
                Dispatcher.UIThread.RunJobs();
                Assert.Null(picker.Chosen);
                Assert.True(picker.IsVisible);

                var escape = KeyNames.Resolve("Esc")!.Value;
                hook.Press(escape, down: true);
                hook.Press(escape, down: false);
                Dispatcher.UIThread.RunJobs();
                Assert.False(picker.IsVisible);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    [Fact]
    public void A_window_frame_keeps_its_target_handle_and_is_not_topmost()
    {
        Ui.Run(() =>
        {
            var frame = ScreenFrameWindow.Show(new ScreenPoint(10, 10), new ScreenSize(40, 30), 12345);
            Assert.NotNull(frame);
            Assert.Equal(12345, frame.TargetHandle);
            Assert.False(frame.Topmost);
            frame.Close();
        });
    }
}
