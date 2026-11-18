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

/// <summary>
/// The window and point pickers, driven through the same global hook a keyboard would use.
/// </summary>
public class WindowRulePickerTests
{
    /// <summary>
    /// 按 F1 与按那颗「取窗口」是同一件事：到桌面上指一扇。指的那一会儿这个窗口让开，取回来之后
    /// 规则已经填好；F1 的按下与抬起都被吞掉，不漏到别的程序里。
    /// </summary>
    [Fact]
    public void F1_takes_a_window_and_both_edges_are_swallowed()
    {
        Ui.RunAsync(async () =>
        {
            var hook = new KeyHook();
            var windows = new WindowsForChecks().Add(7, "Game", "game", "GameClass");
            var devices = new ChecksDevices { Windows = windows, Hook = hook };
            var picker = new WindowRuleWindow(devices.Windows, devices.Hook, null);

            // 测试会话里的窗口不在桌面的清单上：把它交给让位的那一段，才看得见"让开"这件事。
            var desk = AppWindows.Open;
            var steppedAside = false;
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();
                AppWindows.Open = () => [picker];

                picker.Picking = () =>
                {
                    steppedAside = !picker.IsVisible;
                    return Task.FromResult<WindowInfo?>(new WindowInfo(7, "Game",
                        new ScreenPoint(0, 0), new ScreenSize(100, 100), false, false));
                };

                var f1 = KeyNames.Resolve("F1")!.Value;
                Assert.True(hook.Press(f1, down: true));
                Assert.True(hook.Press(f1, down: false));

                var match = picker.FindControl<Avalonia.Controls.TextBox>("MatchBox");
                await Ui.Until(() => match?.Text == "Game", "the picked window did not reach the form");

                Assert.True(steppedAside);
                Assert.True(picker.FindControl<Avalonia.Controls.Button>("UseButton")!.IsEnabled);

                picker.FindControl<Avalonia.Controls.Button>("UseButton")!
                    .RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(picker.Chosen);
                Assert.Equal(7, picker.Chosen!.Handle);
                Assert.Equal("game", picker.Chosen.Process);
                Assert.Equal("GameClass", picker.Chosen.ClassName);
                return true;
            }
            finally
            {
                AppWindows.Open = desk;
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 鲸灵自己的窗口不是宏要操作的那一个：指针停在它上面时框不起来，也取不走。主窗口这类
    /// 不进让位名单的窗口仍可能露着，这条按程序名把它认出来。
    /// </summary>
    [Fact]
    public void The_picker_never_takes_whalegenies_own_window()
    {
        Ui.Run(() =>
        {
            var own = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath!);
            var windows = new WindowsForChecks().Add(8, "WhaleGenie", own, "Window");
            var picker = new WindowPickWindow(windows, new ScreenAt(new ScreenPoint(50, 50)), null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                picker.ProbeAt(50, 50);
                Assert.Null(picker.Hovered);

                picker.Confirm();
                Assert.Null(picker.Chosen);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 指到哪扇就框哪扇：框的是整扇窗口，连边框一起，而不是指针那一点；点一下把它取走。
    /// </summary>
    [Fact]
    public void Pointing_at_a_window_frames_the_whole_of_it_and_a_click_takes_it()
    {
        Ui.Run(() =>
        {
            var windows = new WindowsForChecks()
                .Add(7, "Game", "game", "GameClass")
                .Place(7, 100, 100, 300, 200);
            var picker = new WindowPickWindow(windows, new ScreenAt(new ScreenPoint(150, 150)), null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                picker.ProbeAt(150, 150);

                Assert.Equal(7, picker.Hovered!.Handle);
                Assert.Equal(new PixelPoint(97, 97), picker.Position);
                Assert.Equal(306, picker.Width);
                Assert.Equal(206, picker.Height);

                picker.Confirm();
                Assert.Equal(7, picker.Chosen!.Handle);
                Assert.False(picker.IsVisible);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 取走窗口与框选区域是同一套确认方式：悬浮时框住它，按 F1 确认，Esc 走开。左键不再取走，
    /// 免得点在目标窗口上顺手按到人家的按钮。
    /// </summary>
    [Fact]
    public void F1_takes_the_window_that_is_framed()
    {
        Ui.Run(() =>
        {
            var hook = new KeyHook();
            var windows = new WindowsForChecks()
                .Add(7, "Game", "game", "GameClass")
                .Place(7, 100, 100, 300, 200);
            var picker = new WindowPickWindow(windows, new ScreenAt(new ScreenPoint(150, 150)), hook);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                picker.ProbeAt(150, 150);
                var f1 = KeyNames.Resolve("F1")!.Value;
                Assert.True(hook.Press(f1, down: true));
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(7, picker.Chosen!.Handle);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 拾取时左键是放行的：人还要去点别的窗口、点任务栏启动一个应用，把要取的那扇摆到前面来。
    /// 点击不取走，也不吞掉。
    /// </summary>
    [Fact]
    public void A_click_goes_through_and_does_not_take_the_window()
    {
        Ui.Run(() =>
        {
            var hook = new KeyHook();
            var windows = new WindowsForChecks()
                .Add(7, "Game", "game", "GameClass")
                .Place(7, 0, 0, 200, 200);
            var picker = new WindowPickWindow(windows, new ScreenAt(new ScreenPoint(50, 50)), hook);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();
                picker.ProbeAt(50, 50);

                Assert.False(hook.Click(50, 50));
                Dispatcher.UIThread.RunJobs();

                Assert.Null(picker.Chosen);
                Assert.True(picker.IsVisible);
                Assert.Equal(7, picker.Hovered!.Handle);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 一扇没有标题的窗口框得出来，却不该拿去当目标：那只能按句柄认，而句柄是最容易过期的一格。
    /// 拾取器把话说明白，留着继续挑。
    /// </summary>
    [Fact]
    public void A_window_with_no_title_is_shown_but_not_taken()
    {
        Ui.Run(() =>
        {
            var windows = new WindowsForChecks()
                .Add(9, string.Empty, "game", "GameClass")
                .Place(9, 0, 0, 200, 200);
            var picker = new WindowPickWindow(windows, new ScreenAt(new ScreenPoint(60, 60)), null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                picker.ProbeAt(60, 60);
                Assert.Equal(9, picker.Hovered!.Handle);

                picker.Confirm();
                Assert.Null(picker.Chosen);
                Assert.True(picker.IsVisible);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 列表里点一条就是选它：不必先按 F1。点完之后规则里连程序名与窗口类一起记下，标题在上面
    /// 写着，下面写现在会认到几个窗口；主按钮这时才可用。
    /// </summary>
    [Fact]
    public void A_window_can_be_chosen_from_the_list_without_pressing_f1()
    {
        Ui.Run(() =>
        {
            var windows = new WindowsForChecks()
                .Add(7, "Game", "game", "GameClass")
                .Add(8, "Editor", "notepad", "Notepad");
            var devices = new ChecksDevices { Windows = windows, Hook = new KeyHook() };
            var picker = new WindowRuleWindow(devices.Windows, devices.Hook, null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                var list = picker.FindControl<ListBox>("WindowList")!;
                var chosen = Assert.Single(
                    list.ItemsSource!.Cast<WindowEntry>(), entry => entry.Title == "Editor");
                list.SelectedItem = chosen;
                Dispatcher.UIThread.RunJobs();

                Assert.Equal("Editor", picker.FindControl<TextBox>("MatchBox")!.Text);
                Assert.Equal("Editor", picker.FindControl<TextBlock>("ChosenText")!.Text);
                Assert.Contains("notepad", picker.FindControl<TextBlock>("FactsText")!.Text,
                    StringComparison.Ordinal);
                Assert.True(picker.FindControl<Button>("UseButton")!.IsEnabled);
                Assert.Contains(Strings.Format("WindowRule.Matches", 1),
                    picker.FindControl<TextBlock>("PreviewText")!.Text, StringComparison.Ordinal);

                picker.FindControl<Button>("UseButton")!
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();

                Assert.NotNull(picker.Chosen);
                Assert.Equal(8, picker.Chosen!.Handle);
                Assert.Equal("notepad", picker.Chosen.Process);
            }
            finally
            {
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 列表里没有鲸灵自己，也按搜索框留下的那些走：这两种情况下"选错窗口"最容易发生，而错了
    /// 之后下面的预览会一直显示认到的是自己。
    /// </summary>
    [Fact]
    public void The_list_leaves_out_whalegenies_own_window_and_answers_the_search_box()
    {
        Ui.Run(() =>
        {
            var own = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath!);
            var windows = new WindowsForChecks()
                .Add(7, "Game", "game", "GameClass")
                .Add(8, "Editor", "notepad", "Notepad")
                .Add(9, "WhaleGenie", own, "Window");
            var devices = new ChecksDevices { Windows = windows, Hook = new KeyHook() };
            var picker = new WindowRuleWindow(devices.Windows, devices.Hook, null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                var list = picker.FindControl<ListBox>("WindowList")!;
                Assert.Equal(["Game", "Editor"], Titles(list));
                Assert.False(picker.FindControl<Button>("UseButton")!.IsEnabled);

                picker.FindControl<TextBox>("SearchBox")!.Text = "note";
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(["Editor"], Titles(list));
            }
            finally
            {
                picker.Close();
            }
        });
    }

    /// <summary>
    /// 一条谁都认不到的规则要说清楚"现在一个也认不到"，但仍旧可以拿去用：目标窗口常常是宏自己
    /// 稍后才打开的，此刻认不到不等于规则写错了。这句话是提醒，不是禁令。
    /// </summary>
    [Fact]
    public void A_rule_that_matches_nothing_says_so_and_is_still_allowed()
    {
        Ui.Run(() =>
        {
            var windows = new WindowsForChecks().Add(7, "Game", "game", "GameClass");
            var devices = new ChecksDevices { Windows = windows, Hook = new KeyHook() };
            var picker = new WindowRuleWindow(devices.Windows, devices.Hook, null);
            try
            {
                picker.Show();
                Dispatcher.UIThread.RunJobs();

                picker.FindControl<ListBox>("WindowList")!.SelectedIndex = 0;
                Dispatcher.UIThread.RunJobs();
                picker.FindControl<TextBox>("MatchBox")!.Text = "nothing like this";
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(Strings.Get("WindowRule.NoneMatch"),
                    picker.FindControl<TextBlock>("PreviewText")!.Text);
                Assert.True(picker.FindControl<Button>("UseButton")!.IsEnabled);
            }
            finally
            {
                picker.Close();
            }
        });
    }

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

                var view = window.View;
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

        private Action<MouseHookEvent>? _mouseDown;

        public event Action<MouseHookEvent>? MouseDown
        {
            add => _mouseDown += value;
            remove => _mouseDown -= value;
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

        /// <summary>
        /// 按下鼠标左键，返回它有没有被拾取器吞掉。取窗口时这一下要放行：人还要去点别的窗口、
        /// 点任务栏启动应用，所以"吞掉"才是错的。
        /// </summary>
        public bool Click(int x, int y, HookMouseButton button = HookMouseButton.Left)
        {
            var e = new MouseHookEvent(button, x, y, injected: false);
            _mouseDown?.Invoke(e);
            return e.Suppress;
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
