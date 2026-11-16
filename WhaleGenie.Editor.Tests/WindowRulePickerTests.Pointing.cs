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

// Pointing at a window frames all of it, and what a click does there.
public partial class WindowRulePickerTests
{
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
}
