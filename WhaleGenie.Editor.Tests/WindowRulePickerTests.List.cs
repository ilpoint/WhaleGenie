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

// The list of windows: choosing one without F1, and what it leaves out.
public partial class WindowRulePickerTests
{
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
}
