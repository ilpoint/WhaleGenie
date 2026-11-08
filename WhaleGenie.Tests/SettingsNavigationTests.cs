using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// 设置窗口的导航：顶上三个分组、左边只列当前分组的那几页、换页之后画面上只剩那一页。
/// 分组名在 XAML 里（三个按钮），页面清单也在 XAML 里，所以这里既读控件也读 ViewModel 的选中页。
/// </summary>
public class SettingsNavigationTests
{
    private static readonly string[] Pages =
    [
        "General", "Hotkeys", "Screen", "Input", "Devices",
        "MenuLauncher", "WindowRules", "WheelMenu", "WheelActions", "Theme",
    ];

    [Fact]
    public void The_panel_lists_every_page_once_in_the_order_it_is_read_in()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var nav = Assert.IsType<FANavigationView>(window.FindControl<FANavigationView>("Nav"));
            var listed = nav.MenuItems.OfType<FANavigationViewItem>()
                .Select(item => item.Tag as string)
                .ToList();

            Assert.Equal(Pages, listed);

            // 分组名搬到了窗口顶上，所以列表里不再有标题项：标题不该挤在页面之间。
            Assert.Empty(nav.MenuItems.OfType<FANavigationViewItemHeader>());

            window.Close();
        });
    }

    /// <summary>
    /// 顶上的三个按钮就是原来那三个标题：系统、触发器、外观。它们在窗口最上方，按下的那个是
    /// 当前页所在的分组。
    /// </summary>
    [Fact]
    public void The_top_of_the_window_names_the_three_groups()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var tabs = window.GetVisualDescendants()
                .OfType<Button>()
                .Where(button => button.Classes.Contains("Tab") && button.Classes.Contains("Root"))
                .ToList();

            Assert.Equal(3, tabs.Count);
            Assert.Equal(
                new[] { nameof(SettingsRoot.System), nameof(SettingsRoot.Triggers), nameof(SettingsRoot.Appearance) },
                tabs.Select(tab => tab.Tag as string ?? string.Empty).ToArray());

            // 开在通用页，所以按下的是「系统」这一组。
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(viewModel.IsSystemRoot);
            Assert.False(viewModel.IsTriggersRoot);

            window.Close();
        });
    }

    /// <summary>
    /// 左边只列当前分组的那几页：开在触发器组的窗口规则页时，系统组的五页不出现；换到「系统」
    /// 这一组则落到该组第一页，列表也跟着换。
    /// </summary>
    [Fact]
    public void The_list_carries_only_the_pages_of_the_open_group()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WindowRules);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var nav = Assert.IsType<FANavigationView>(window.FindControl<FANavigationView>("Nav"));
            Assert.Equal(
                new[] { "MenuLauncher", "WindowRules", "WheelMenu", "WheelActions" },
                Shown(nav));

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            viewModel.SetRoot(SettingsRoot.System);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(SettingsSubPage.General, viewModel.SelectedPage);
            Assert.Equal(new[] { "General", "Hotkeys", "Screen", "Input", "Devices" }, Shown(nav));

            // 外观组只有一页，切过去时列表也只留那一项。
            viewModel.SetRoot(SettingsRoot.Appearance);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(SettingsSubPage.Theme, viewModel.SelectedPage);
            Assert.Equal(new[] { "Theme" }, Shown(nav));

            window.Close();
        });
    }

    private static string[] Shown(FANavigationView nav)
        => nav.MenuItems.OfType<FANavigationViewItem>()
            .Where(item => item.IsEffectivelyVisible)
            .Select(item => item.Tag as string ?? string.Empty)
            .ToArray();

    [Fact]
    public void Opening_on_a_page_selects_the_item_that_stands_for_it()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WindowRules);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.Equal(SettingsSubPage.WindowRules, viewModel.SelectedPage);
            Assert.True(viewModel.IsWindowRulesPage);

            var nav = Assert.IsType<FANavigationView>(window.FindControl<FANavigationView>("Nav"));
            var selected = Assert.IsType<FANavigationViewItem>(nav.SelectedItem);
            Assert.Equal("WindowRules", selected.Tag as string);

            window.Close();
        });
    }

    [Fact]
    public void Only_the_page_that_was_chosen_is_drawn()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            viewModel.SetPage(SettingsSubPage.Screen);
            Dispatcher.UIThread.RunJobs();

            var headings = window.GetVisualDescendants()
                .OfType<FASettingsExpander>()
                .Where(expander => expander.IsEffectivelyVisible)
                .Select(expander => expander.Header as string)
                .OfType<string>()
                .ToList();

            // 屏幕捕获页的两张卡：读画方式与取帧。
            Assert.Contains(Strings.Get("Settings.Reading"), headings);
            Assert.Contains(Strings.Get("Settings.FramesCard"), headings);
            Assert.DoesNotContain(Strings.Get("Settings.Nav.Devices"), headings);
            Assert.DoesNotContain(Strings.Get("Settings.Nav.General"), headings);

            window.Close();
        });
    }

    /// <summary>
    /// 代码后置按名字找的那些控件，在文件里必须只出现一次。重名在无头测试里不会报错，真实窗口一
    /// 打开就抛「Control with the name ... already registered」，所以把这条做成能失败的断言。
    /// </summary>
    [Fact]
    public void Every_control_the_window_looks_up_by_name_is_named_once()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views",
            "SettingsWindow.axaml"));

        string[] looked =
        [
            "Nav", "DoneButton", "SettingsPanel", "StorageProblem",
            "CaptureSystemHotkeyButton", "ClearSystemHotkeyButton", "CaptureRecordStopButton",
            "PreloadOcrCheck", "DriverInstallButton", "DriverAttachCheck",
            "RunAsAdminCheck", "AdminRestartButton",
            "MenuRuleList", "AddMenuRulePageButton", "EditMenuRuleButton", "RemoveMenuRuleButton",
            "CaptureMenuLauncherButton", "ClearMenuLauncherButton",
            "PreviewWheelButton", "WheelEditorHost", "FrameRateBox", "ThemeSelect",
            "CustomizeThemeButton",
        ];

        foreach (var name in looked)
        {
            var count = Regex.Matches(xaml, $"Name=\"{name}\"", RegexOptions.None).Count;
            Assert.True(count == 1, $"{name} is named {count} times in the settings window");
        }
    }

    private static string Repository()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null;
             folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "WhaleGenie.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("the repository root was not found");
    }

    /// <summary>
    /// 屏幕捕获页的两项：取帧上限的输入框最高只能是显示器刷新率，复用关着时保留时长那格不让人改。
    /// </summary>
    [Fact]
    public void The_screen_page_holds_the_cap_and_the_reuse_switch()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            viewModel.SetPage(SettingsSubPage.Screen);
            Dispatcher.UIThread.RunJobs();

            var cap = Assert.IsType<FANumberBox>(window.FindControl<FANumberBox>("FrameRateBox"));
            Assert.Equal(viewModel.FastestRate, (int)cap.Maximum);
            Assert.True(viewModel.FrameRateFollow);
            Assert.False(viewModel.FrameRateEnabled);

            viewModel.FrameCache = true;
            Assert.True(viewModel.FrameCacheAgeEnabled);

            window.Close();
        });
    }
}
