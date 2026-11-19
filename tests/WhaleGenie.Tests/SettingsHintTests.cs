using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// 行内说明的气泡：350 毫秒后只开一次，指针在图标或面板上就保持，离开 200 毫秒后收起，
/// 点击固定、再点或 Esc 或点别处收起；同一窗口只有一个气泡，换页与关窗不留残影。
/// 等待用假时钟推进，指针位置用顶层坐标注入，不依赖真实鼠标。
/// </summary>
public class SettingsHintTests
{
    private static readonly TimeSpan Hover = TimeSpan.FromMilliseconds(350);
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(200);

    [Fact]
    public void An_empty_hint_takes_no_room_and_a_cleared_hint_closes()
    {
        Ui.Run(() =>
        {
            var bare = new HintInfo();
            Assert.False(bare.IsVisible);

            bare.Text = "一句说明";
            Assert.True(bare.IsVisible);

            var setup = ShowHint();
            Open(setup);
            Assert.True(setup.Hint.IsBubbleOpen);

            setup.Hint.Text = "   ";
            Dispatcher.UIThread.RunJobs();

            Assert.False(setup.Hint.IsVisible);
            Assert.Equal(HintBubbleState.Closed, setup.Hint.State);
            Assert.Equal(HintBubbleCloseReason.TextCleared, setup.Host.LastCloseReason);
            setup.Window.Close();
        });
    }

    [Fact]
    public void Hovering_opens_once_after_the_agreed_wait()
    {
        Ui.Run(() =>
        {
            var setup = ShowHint();

            setup.Host.NotifyPointerOver(setup.Hint, IconPoint(setup));
            setup.Delays.Advance(Hover - TimeSpan.FromMilliseconds(1));

            Assert.Equal(HintBubbleState.HoverPending, setup.Host.State);
            Assert.Equal(0, setup.Host.OpenCount);

            setup.Delays.Advance(TimeSpan.FromMilliseconds(1));

            Assert.Equal(HintBubbleState.Open, setup.Host.State);
            Assert.Equal(1, setup.Host.OpenCount);
            Assert.True(setup.Host.IsPanelOpen);

            setup.Delays.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(1, setup.Host.OpenCount);
            setup.Window.Close();
        });
    }

    [Fact]
    public void A_bubble_stays_open_while_the_pointer_is_on_icon_or_panel()
    {
        Ui.Run(() =>
        {
            var setup = ShowHint();
            Open(setup);

            var away = OutsidePoint(setup);
            for (var turn = 0; turn < 5; turn++)
            {
                setup.Host.NotifyPointerLeft(setup.Hint, away);
                setup.Host.NotifyPointerOver(setup.Hint, IconPoint(setup));
            }

            setup.Host.NotifyPointerMoved(setup.Host.PanelRect.Center);
            setup.Delays.Advance(TimeSpan.FromSeconds(3));

            Assert.Equal(HintBubbleState.Open, setup.Host.State);
            Assert.Equal(1, setup.Host.OpenCount);
            Assert.True(setup.Host.IsPanelOpen);
            setup.Window.Close();
        });
    }

    [Fact]
    public void Leaving_waits_the_grace_before_closing()
    {
        Ui.Run(() =>
        {
            var setup = ShowHint();
            Open(setup);

            setup.Host.NotifyPointerMoved(OutsidePoint(setup));
            setup.Delays.Advance(Grace - TimeSpan.FromMilliseconds(1));
            Assert.Equal(HintBubbleState.Closing, setup.Host.State);
            Assert.True(setup.Host.IsPanelOpen);

            setup.Delays.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Equal(HintBubbleState.Closed, setup.Host.State);
            Assert.Equal(HintBubbleCloseReason.PointerLeft, setup.Host.LastCloseReason);

            setup.Host.NotifyPointerOver(setup.Hint, IconPoint(setup));
            setup.Delays.Advance(Hover);
            setup.Host.NotifyPointerMoved(OutsidePoint(setup));
            setup.Delays.Advance(Grace / 2);
            setup.Host.NotifyPointerMoved(setup.Host.PanelRect.Center);
            setup.Delays.Advance(Grace);

            Assert.Equal(HintBubbleState.Open, setup.Host.State);
            setup.Window.Close();
        });
    }

    [Fact]
    public void A_click_pins_and_a_second_click_puts_it_away()
    {
        Ui.Run(() =>
        {
            var setup = ShowHint();

            setup.Hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(setup.Hint.IsBubbleOpen);
            Assert.True(setup.Hint.IsBubblePinned);
            Assert.Equal(1, setup.Host.OpenCount);

            setup.Host.NotifyPointerMoved(OutsidePoint(setup));
            setup.Delays.Advance(TimeSpan.FromSeconds(3));
            Assert.Equal(HintBubbleState.Pinned, setup.Host.State);

            setup.Hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(setup.Hint.IsBubbleOpen);
            Assert.False(setup.Hint.IsBubblePinned);
            Assert.Equal(HintBubbleCloseReason.Toggled, setup.Host.LastCloseReason);
            setup.Window.Close();
        });
    }

    [Fact]
    public void Escape_and_a_press_outside_close_a_pinned_bubble()
    {
        Ui.Run(() =>
        {
            var setup = ShowHint();
            setup.Hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(setup.Hint.IsBubblePinned);

            setup.Window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });

            Assert.False(setup.Hint.IsBubbleOpen);
            Assert.Equal(HintBubbleCloseReason.Escape, setup.Host.LastCloseReason);

            setup.Hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(setup.Hint.IsBubblePinned);
            setup.Window.MouseDown(OutsidePoint(setup), MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.False(setup.Hint.IsBubbleOpen);
            Assert.False(setup.Hint.IsBubblePinned);
            Assert.Equal(HintBubbleCloseReason.OutsidePress, setup.Host.LastCloseReason);
            setup.Window.Close();
        });
    }

    [Fact]
    public void Opening_another_hint_closes_the_first()
    {
        Ui.Run(() =>
        {
            var first = new HintInfo { Text = "第一条" };
            var second = new HintInfo { Text = "第二条" };
            var window = new Window { Width = 600, Height = 400 };
            window.Content = new StackPanel { Spacing = 40, Children = { first, second } };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var host = HintBubbleHost.For(window);
            var delays = new ManualHintDelay();
            host.Delays = delays;
            var setup = new HintSetup(window, first, host, delays);

            Open(setup);
            Assert.True(first.IsBubbleOpen);

            host.NotifyPointerOver(second, Center(second, window));
            delays.Advance(Hover);

            Assert.False(first.IsBubbleOpen);
            Assert.True(second.IsBubbleOpen);
            Assert.Same(second, host.CurrentOwner);
            Assert.Equal(2, host.OpenCount);
            Assert.Equal(HintBubbleCloseReason.AnotherOpened, host.LastCloseReason);
            Assert.Single(new[] { first, second }, hint => hint.IsBubbleOpen);
            window.Close();
        });
    }

    [Fact]
    public void Escape_in_the_settings_window_closes_the_bubble_before_the_window()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var hint = window.GetVisualDescendants().OfType<HintInfo>()
                .First(candidate => candidate.IsEffectivelyVisible && candidate.Text?.Length > 0);
            var host = HintBubbleHost.For(window);
            host.Delays = new ManualHintDelay();

            hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(hint.IsBubblePinned);

            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsVisible);
            Assert.False(hint.IsBubbleOpen);
            Assert.Equal(HintBubbleCloseReason.Escape, host.LastCloseReason);

            window.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            });
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void A_page_change_hides_the_bubble_at_once()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var hint = window.GetVisualDescendants().OfType<HintInfo>()
                .First(candidate => candidate.IsEffectivelyVisible && candidate.Text?.Length > 0);
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            var host = HintBubbleHost.For(window);
            var delays = new ManualHintDelay();
            host.Delays = delays;

            host.NotifyPointerOver(hint, Center(hint, window));
            delays.Advance(Hover);
            Assert.True(hint.IsBubbleOpen);

            viewModel.SetPage(SettingsSubPage.Screen);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(HintBubbleState.Closed, host.State);
            Assert.Equal(HintBubbleCloseReason.OwnerHidden, host.LastCloseReason);
            delays.Advance(TimeSpan.FromSeconds(1));
            Assert.Equal(HintBubbleState.Closed, host.State);
            window.Close();
        });
    }

    [Fact]
    public void Hiding_the_window_takes_the_bubble_with_it()
    {
        Ui.Run(() =>
        {
            var setup = ShowHint();
            Open(setup);
            Assert.True(setup.Hint.IsBubbleOpen);

            setup.Window.Hide();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(HintBubbleState.Closed, setup.Host.State);
            Assert.Equal(HintBubbleCloseReason.WindowHidden, setup.Host.LastCloseReason);

            setup.Window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(HintBubbleState.Closed, setup.Host.State);
            setup.Window.Close();
        });
    }

    [Fact]
    public void The_panel_keeps_its_cap_and_lets_the_text_be_selected()
    {
        Ui.Run(() =>
        {
            var text = new string('说', 240);
            var setup = ShowHint(text);
            Open(setup);

            var panel = Assert.IsType<Border>(setup.Host.Panel);
            Assert.True(panel.MaxWidth <= 360);

            var scroll = Assert.IsType<ScrollViewer>(panel.Child);
            Assert.True(scroll.MaxHeight <= 320);
            var body = Assert.IsType<SelectableTextBlock>(scroll.Content);
            Assert.Equal(text, body.Text);
            setup.Window.Close();
        });
    }

    [Fact]
    public void The_panel_places_itself_inside_the_available_area()
    {
        var available = new Rect(0, 0, 800, 600);
        var panel = new Size(280, 96);

        var below = HintBubblePlacement.Place(new Rect(100, 100, 20, 20), available, panel);
        Assert.True(available.Contains(below));
        Assert.Equal(126, below.Y);

        var flipped = HintBubblePlacement.Place(new Rect(100, 540, 20, 20), available, panel);
        Assert.True(available.Contains(flipped));
        Assert.True(flipped.Bottom < 540);

        var atRight = HintBubblePlacement.Place(new Rect(790, 100, 10, 10), available, panel);
        Assert.True(available.Contains(atRight));
        Assert.Equal(available.Right, atRight.Right);

        var bottomRow = HintBubblePlacement.Place(new Rect(10, 580, 20, 20), available, panel);
        Assert.True(available.Contains(bottomRow));

        Assert.Equal(below, HintBubblePlacement.Place(new Rect(100, 100, 20, 20), available, panel));
    }

    [Fact]
    public void Scrolling_does_not_reopen_the_bubble()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var hint = window.GetVisualDescendants().OfType<HintInfo>()
                .First(candidate => candidate.IsEffectivelyVisible && candidate.Text?.Length > 0);
            var host = HintBubbleHost.For(window);
            var delays = new ManualHintDelay();
            host.Delays = delays;

            host.NotifyPointerOver(hint, Center(hint, window));
            delays.Advance(Hover);
            var opened = host.OpenCount;
            Assert.Equal(HintBubbleState.Open, host.State);

            var scroll = window.FindControl<ScrollViewer>("SettingsScroll")!;
            scroll.Offset = new Vector(0, 10);
            Dispatcher.UIThread.RunJobs();
            delays.Advance(TimeSpan.FromSeconds(1));

            Assert.Equal(opened, host.OpenCount);
            Assert.NotEqual(HintBubbleState.HoverPending, host.State);
            window.Close();
        });
    }

    [Fact]
    public void Other_windows_keep_their_hints()
    {
        Ui.Run(() =>
        {
            var left = ShowHint();
            var right = ShowHint();

            Assert.NotSame(left.Host, right.Host);

            left.Hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            right.Hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(left.Hint.IsBubblePinned);
            Assert.True(right.Hint.IsBubblePinned);

            left.Hint.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(left.Hint.IsBubbleOpen);
            Assert.True(right.Hint.IsBubbleOpen);

            left.Window.Close();
            right.Window.Close();
        });
    }

    private static HintSetup ShowHint(string text = "一句说明")
    {
        var hint = new HintInfo { Text = text };
        var window = new Window { Width = 600, Height = 400 };
        window.Content = new StackPanel { Children = { hint } };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var host = HintBubbleHost.For(window);
        var delays = new ManualHintDelay();
        host.Delays = delays;
        return new HintSetup(window, hint, host, delays);
    }

    private static void Open(HintSetup setup)
    {
        setup.Host.NotifyPointerOver(setup.Hint, IconPoint(setup));
        setup.Delays.Advance(Hover);
    }

    private static Point IconPoint(HintSetup setup) => Center(setup.Hint, setup.Window);

    private static Point Center(Control control, TopLevel top)
        => control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;

    private static Point OutsidePoint(HintSetup setup)
        => new(setup.Window.Bounds.Width - 10, setup.Window.Bounds.Height - 10);

    private sealed record HintSetup(
        Window Window, HintInfo Hint, HintBubbleHost Host, ManualHintDelay Delays);
}
