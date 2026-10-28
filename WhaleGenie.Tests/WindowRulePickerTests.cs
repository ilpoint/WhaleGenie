using System;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The window and point pickers, driven through the same global hook a keyboard would use.
/// </summary>
public class WindowRulePickerTests
{
    [Fact]
    public void F1_captures_the_foreground_window_and_both_edges_are_swallowed()
    {
        Ui.Run(() =>
        {
            var hook = new KeyHook();
            var windows = new WindowsForChecks().Add(7, "Game", "game", "GameClass");
            var devices = new ChecksDevices { Windows = windows, Hook = hook };
            var picker = new WindowRuleWindow(devices.Windows, devices.Hook, null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                var f1 = KeyNames.Resolve("F1")!.Value;
                Assert.True(hook.Press(f1, down: true));
                Dispatcher.UIThread.RunJobs();
                Assert.True(hook.Press(f1, down: false));

                var match = picker.FindControl<Avalonia.Controls.TextBox>("MatchBox");
                Assert.NotNull(match);
                Assert.Equal("Game", match.Text);
                Assert.True(picker.FindControl<Avalonia.Controls.Button>("UseButton")!.IsVisible);

                picker.FindControl<Avalonia.Controls.Button>("UseButton")!
                    .RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(picker.Chosen);
                Assert.Equal(7, picker.Chosen!.Handle);
                Assert.Equal("game", picker.Chosen.Process);
                Assert.Equal("GameClass", picker.Chosen.ClassName);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    [Fact]
    public void F1_ignores_whalegenies_own_window()
    {
        Ui.Run(() =>
        {
            var own = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath!);
            var windows = new WindowsForChecks().Add(8, "WhaleGenie", own, "Window");
            var hook = new KeyHook();
            var devices = new ChecksDevices { Windows = windows, Hook = hook };
            var picker = new WindowRuleWindow(devices.Windows, devices.Hook, null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                var status = picker.FindControl<Avalonia.Controls.TextBlock>("Status");
                Assert.Equal(Strings.Get("WindowRule.Waiting"), status?.Text);

                // The device's foreground reader is the first listed window; the picker refuses it
                // because it is this program rather than the one the macro should use.
                hook.Press(KeyNames.Resolve("F1")!.Value, down: true);
                Dispatcher.UIThread.RunJobs();
                hook.Press(KeyNames.Resolve("F1")!.Value, down: false);

                Assert.Equal(Strings.Get("WindowRule.OwnWindow"), status?.Text);
                Assert.False(picker.FindControl<Avalonia.Controls.Button>("UseButton")!.IsVisible);
            }
            finally
            {
                picker.Close();
            }
        });
    }

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

    [Fact]
    public void AltX_writes_the_action_window_position()
    {
        Ui.Run(() =>
        {
            var devices = new ChecksDevices { Screen = new ScreenAt(new ScreenPoint(321, 123)) };
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [])
            {
                Devices = devices,
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var view = Assert.IsType<ActionEditorView>(window.Content);
                view.SelectAction("vision.getPixel");
                Dispatcher.UIThread.RunJobs();

                window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                var action = Assert.IsType<AddActionViewModel>(view.DataContext);
                Assert.Equal("321", action.Parameters
                    .First(parameter => parameter.Definition.Name == "x").CurrentText);
                Assert.Equal("123", action.Parameters
                    .First(parameter => parameter.Definition.Name == "y").CurrentText);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class KeyHook : IInputHook
    {
        public event Action<KeyHookEvent>? KeyDown;

        public event Action<KeyHookEvent>? KeyUp;

        public event Action<MouseHookEvent>? MouseDown
        {
            add { }
            remove { }
        }

        public event Action<MouseHookEvent>? MouseUp
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

        public bool Press(HookKey key, bool down)
        {
            var e = new KeyHookEvent(key, injected: false);
            if (down)
            {
                KeyDown?.Invoke(e);
            }
            else
            {
                KeyUp?.Invoke(e);
            }

            return e.Suppress;
        }
    }

    private sealed class ScreenAt(ScreenPoint cursor) : IScreenDevice
    {
        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        public ScreenPoint Cursor { get; } = cursor;

        public PixelColor PixelAt(int x, int y) => default;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => NullDeviceLayer.Instance.Screen.Capture(request);
    }

    private sealed class UiAt(UiElementInfo element) : IUiDevice
    {
        public bool Exists(UiQuery query, int timeoutMs)
            => NullDeviceLayer.Instance.Ui.Exists(query, timeoutMs);

        public UiElementInfo? ElementAt(int x, int y) => element;

        public bool Click(UiQuery query, string button)
            => NullDeviceLayer.Instance.Ui.Click(query, button);

        public bool Invoke(UiQuery query) => NullDeviceLayer.Instance.Ui.Invoke(query);

        public UiElementFacts? Facts(UiQuery query)
            => NullDeviceLayer.Instance.Ui.Facts(query);

        public bool FocusWindow(UiQuery query)
            => NullDeviceLayer.Instance.Ui.FocusWindow(query);

        public bool SetText(UiQuery query, string text, bool clearFirst)
            => NullDeviceLayer.Instance.Ui.SetText(query, text, clearFirst);

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
            => NullDeviceLayer.Instance.Ui.FindAll(query, limit);

        public bool Select(UiQuery query, string text, int itemIndex)
            => NullDeviceLayer.Instance.Ui.Select(query, text, itemIndex);

        public bool SetChecked(UiQuery query, bool? state)
            => NullDeviceLayer.Instance.Ui.SetChecked(query, state);

        public bool SetExpanded(UiQuery query, string action)
            => NullDeviceLayer.Instance.Ui.SetExpanded(query, action);

        public bool ScrollIntoView(UiQuery query)
            => NullDeviceLayer.Instance.Ui.ScrollIntoView(query);

        public UiTable ReadTable(UiQuery query, int limit)
            => NullDeviceLayer.Instance.Ui.ReadTable(query, limit);
    }
}
