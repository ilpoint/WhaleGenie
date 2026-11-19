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

// What the readout says: a handle rule, the pixel under the crosshair and the basis.
public partial class WindowRulePickerTests
{

    private static List<string> Titles(ListBox list)
        => [.. list.ItemsSource!.Cast<WindowEntry>().Select(entry => entry.Title)];

    [Fact]
    public void A_handle_rule_shows_its_number_without_text_comparison_switches()
    {
        Ui.Run(() =>
        {
            var windows = new WindowsForChecks().Add(7, "Game", "game", "GameClass");
            var devices = new ChecksDevices { Windows = windows, Hook = new KeyHook() };
            var picker = new WindowRuleWindow(devices.Windows, devices.Hook,
                new WindowRule(Title: "Game", Process: "game", ClassName: "GameClass",
                    Handle: 7, How: WindowHow.Handle));
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("7", picker.FindControl<TextBox>("MatchBox")?.Text);
                Assert.False(picker.FindControl<WrapPanel>("ComparisonSwitches")!.IsVisible);
                Assert.True(picker.FindControl<TextBlock>("HandleHint")!.IsVisible);
                Assert.Contains(Strings.Format("WindowRule.Matches", 1),
                    picker.FindControl<TextBlock>("PreviewText")?.Text);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    [Fact]
    public void F1_takes_the_pixel_under_the_crosshair_rather_than_the_uia_element_centre()
    {
        Ui.Run(() =>
        {
            var hook = new KeyHook();
            var screen = new ScreenAt(new ScreenPoint(50, 60));
            var windows = new WindowsForChecks().Add(9, "Game", "game", "GameClass").ClientAt(9, 10, 20);
            var devices = new ChecksDevices { Screen = screen, Windows = windows, Hook = hook };
            var picker = new PointPickerWindow(devices.Screen, devices.Windows, devices.Ui, devices.Hook, new WindowRule(Title: "Game"));
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.True(picker.GetVisualDescendants().OfType<Canvas>()
                    .First(control => control.Name == "Crosshair").IsVisible);
                Assert.Contains("F1", picker.GetVisualDescendants().OfType<TextBlock>()
                    .First(control => control.Name == "ReadoutText").Text,
                    StringComparison.Ordinal);

                var f1 = KeyNames.Resolve("F1")!.Value;
                Assert.True(hook.Press(f1, down: true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(hook.Press(f1, down: false));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(new ScreenPoint(50, 60), picker.Chosen);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    [Fact]
    public void A_window_readout_keeps_the_basis_points_shortcut_and_element_together()
    {
        Ui.Run(() =>
        {
            var hook = new KeyHook();
            var screen = new ScreenAt(new ScreenPoint(50, 60));
            var windows = new WindowsForChecks().Add(9, "Game", "game", "GameClass")
                .ClientAt(9, 10, 20);
            var devices = new ChecksDevices
            {
                Screen = screen,
                Windows = windows,
                Hook = hook,
                Ui = new UiAt(new UiElementInfo("Open", "open", "Button", "Button",
                    new ScreenPoint(40, 50), new ScreenSize(12, 12), "Game")),
            };
            var picker = new PointPickerWindow(devices.Screen, devices.Windows, devices.Ui, devices.Hook, new WindowRule(Title: "Game"));
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                var readout = Assert.Single(picker.GetVisualDescendants().OfType<TextBlock>(),
                    control => control.Name == "ReadoutText");
                Assert.Contains("窗口基准", readout.Text, StringComparison.Ordinal);
                Assert.Contains("窗口 40,40", readout.Text, StringComparison.Ordinal);
                Assert.Contains("屏幕 50,60", readout.Text, StringComparison.Ordinal);
                Assert.Contains("Button · Open", readout.Text, StringComparison.Ordinal);
                Assert.Contains("F1", readout.Text, StringComparison.Ordinal);
                Assert.Contains("Esc", readout.Text, StringComparison.Ordinal);
                Assert.DoesNotContain(picker.GetVisualDescendants().OfType<TextBlock>(),
                    control => control.Name == "HintText");
            }
            finally
            {
                picker.Close();
            }
        });
    }
}
