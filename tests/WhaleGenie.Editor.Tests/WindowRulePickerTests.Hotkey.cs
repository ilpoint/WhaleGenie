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

// F1 takes a window, and this program own window is never taken.
public partial class WindowRulePickerTests
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
}
