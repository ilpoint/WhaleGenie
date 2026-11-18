using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// 设置窗口的导航：左边是三个分组，内容列顶上是当前分组的那几页，换组之后只剩那一组的页签。
/// 分组名与页签都在 XAML 里，所以这里既读控件也读 ViewModel 的选中页；分组本身平铺，没有
/// 展开动作，组数与行数是迁移的基线。
/// </summary>
public class SettingsNavigationTests
{
    private static readonly string[] Groups = ["System", "Triggers", "Appearance"];

    private static readonly SettingsSubPage[] Pages =
    [
        SettingsSubPage.General, SettingsSubPage.Hotkeys, SettingsSubPage.Screen,
        SettingsSubPage.Input, SettingsSubPage.Devices,
        SettingsSubPage.MenuLauncher, SettingsSubPage.WindowRules,
        SettingsSubPage.WheelMenu, SettingsSubPage.WheelActions,
        SettingsSubPage.Theme,
    ];

    [Fact]
    public void The_list_names_the_three_groups_once_in_the_order_it_is_read_in()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var nav = Assert.IsType<FANavigationView>(window.FindControl<FANavigationView>("Nav"));
            var listed = nav.MenuItems.OfType<FANavigationViewItem>()
                .Select(item => item.Tag as string)
                .ToList();

            Assert.Equal(Groups, listed);

            // 分组就是分组：列表里不再夹着页名，也不再夹着标题项。层级由左栏与顶上页签分开说。
            Assert.Empty(nav.MenuItems.OfType<FANavigationViewItemHeader>());

            window.Close();
        });
    }

    /// <summary>
    /// 内容列顶上那一排是当前分组里的页：开在「系统」组时列的是通用、快捷键、屏幕捕获等五页，
    /// 按下的那个是当前页。
    /// </summary>
    [Fact]
    public void The_top_of_the_content_column_names_the_pages_of_the_open_group()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var tabs = Tabs(window);
            Assert.Equal(
                ["General", "Hotkeys", "Screen", "Input", "Devices"],
                tabs.Select(tab => tab.Tag as string ?? string.Empty).ToArray());

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(viewModel.IsSystemRoot);
            Assert.True(viewModel.IsGeneralPage);
            Assert.Single(tabs, tab => tab.Classes.Contains("selected"));

            window.Close();
        });
    }

    /// <summary>
    /// 顶上只列当前分组的那几页：开在触发器组的窗口规则页时，系统组的五页不出现；换到「系统」
    /// 这一组则落到该组第一页，页签也跟着换。
    /// </summary>
    [Fact]
    public void The_tabs_carry_only_the_pages_of_the_open_group()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WindowRules);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(
                ["MenuLauncher", "WindowRules", "WheelMenu", "WheelActions"],
                ShownTabs(window));

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            viewModel.SetRoot(SettingsRoot.System);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(SettingsSubPage.General, viewModel.SelectedPage);
            Assert.Equal(["General", "Hotkeys", "Screen", "Input", "Devices"], ShownTabs(window));

            // 外观组只有一页，切过去时页签也只留那一页。
            viewModel.SetRoot(SettingsRoot.Appearance);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(SettingsSubPage.Theme, viewModel.SelectedPage);
            Assert.Equal(["Theme"], ShownTabs(window));

            window.Close();
        });
    }

    private static string[] ShownTabs(Window window)
        => Tabs(window).Select(tab => tab.Tag as string ?? string.Empty).ToArray();

    private static Button[] Tabs(Window window)
        => window.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("Capsule") && button.IsEffectivelyVisible)
            .ToArray();

    /// <summary>
    /// 打开某一页时，左栏选中它所属的分组，顶上选中它自己：两处选中都从同一个页面来。
    /// </summary>
    [Fact]
    public void Opening_on_a_page_selects_its_group_and_its_tab()
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
            Assert.Equal(nameof(SettingsRoot.Triggers), selected.Tag as string);

            var tab = Assert.Single(Tabs(window), button => button.Classes.Contains("selected"));
            Assert.Equal(nameof(SettingsSubPage.WindowRules), tab.Tag as string);

            window.Close();
        });
    }

    /// <summary>
    /// 左栏每一项都要有图标：只有文字的列表读起来是一串分组名，图标让"这一组回答什么"先于文字到。
    /// </summary>
    [Fact]
    public void Every_group_in_the_list_carries_an_icon()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var nav = Assert.IsType<FANavigationView>(window.FindControl<FANavigationView>("Nav"));
            foreach (var item in nav.MenuItems.OfType<FANavigationViewItem>())
            {
                var content = Assert.IsType<StackPanel>(item.Content);
                Assert.Single(content.Children.OfType<IconView>());
            }

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
                .OfType<SettingsGroup>()
                .Where(group => group.IsEffectivelyVisible)
                .Select(HeaderText)
                .ToList();

            // 屏幕捕获页的两张卡：读画方式与画面捕获。
            Assert.Contains(headings, text => text.Contains(Strings.Get("Settings.Reading")));
            Assert.Contains(headings, text => text.Contains(Strings.Get("Settings.FramesCard")));
            Assert.DoesNotContain(headings, text => text.Contains(Strings.Get("Settings.Nav.Devices")));
            Assert.DoesNotContain(headings, text => text.Contains(Strings.Get("Settings.Nav.General")));

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
        var xaml = ViewSource.MarkupAnd("SettingsWindow.axaml", "SettingsSystemView.axaml",
            "SettingsTriggersView.axaml", "SettingsAppearanceView.axaml");

        string[] looked =
        [
            "Nav", "SettingsPanel", "StorageProblem",
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

    /// <summary>
    /// 行控件原本写在 Expander 里，平铺分组必须仍能独立呈现：行是有效可见的、撑满组宽、
    /// 主体与右侧控件各在其位且不重叠。四项任一不成立就说明行模板依赖父级项宿主。
    /// </summary>
    [Fact]
    public void A_row_renders_outside_the_expander_it_was_written_for()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var group = window.GetVisualDescendants()
                .OfType<SettingsGroup>()
                .First(candidate => candidate.IsEffectivelyVisible);
            var rows = group.GetVisualDescendants()
                .OfType<FASettingsExpanderItem>()
                .Where(row => row.IsEffectivelyVisible)
                .ToList();
            Assert.Equal(4, rows.Count);

            var content = group.GetVisualDescendants()
                .OfType<StackPanel>()
                .First(panel => panel.GetVisualDescendants().OfType<FASettingsExpanderItem>().Any());

            foreach (var row in rows)
            {
                Assert.True(row.Bounds.Width > 0, $"{row.Name} has no width");
                Assert.InRange(row.Bounds.Width, content.Bounds.Width - 1, content.Bounds.Width + 1);
                Assert.NotNull(row.Background);

                var body = Assert.IsAssignableFrom<Visual>(row.Content);
                var footer = Assert.IsAssignableFrom<Visual>(row.Footer);
                var bodyRect = body.TranslatePoint(default, row)!.Value;
                var footerRect = footer.TranslatePoint(default, row)!.Value;
                var bodyArea = new Rect(bodyRect, body.Bounds.Size);
                var footerArea = new Rect(footerRect, footer.Bounds.Size);
                Assert.False(
                    bodyArea.Intersects(footerArea),
                    $"{row.Name} body={bodyArea} footer={footerArea}");
            }

            window.Close();
        });
    }

    /// <summary>
    /// 十个页签的平铺基线：每个分组都完整列出它的行，数出来的组与行不能少，也不能多。
    /// </summary>
    [Theory]
    [InlineData(SettingsSubPage.General, 2, 6)]
    [InlineData(SettingsSubPage.Hotkeys, 2, 4)]
    [InlineData(SettingsSubPage.Screen, 2, 6)]
    [InlineData(SettingsSubPage.Input, 1, 2)]
    [InlineData(SettingsSubPage.Devices, 2, 5)]
    [InlineData(SettingsSubPage.MenuLauncher, 1, 1)]
    [InlineData(SettingsSubPage.WindowRules, 1, 1)]
    [InlineData(SettingsSubPage.WheelMenu, 1, 12)]
    [InlineData(SettingsSubPage.WheelActions, 1, 1)]
    [InlineData(SettingsSubPage.Theme, 1, 2)]
    public void Every_page_lists_its_groups_and_rows_flat(
        SettingsSubPage page, int groups, int rows)
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);

            viewModel.SetPage(page);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal((groups, rows), Count(window));
            window.Close();
        });
    }

    /// <summary>
    /// 三份页面视图里不再有分组级的 Expander：静下来看是一条平铺的清单，而不是一排能收起的卡片。
    /// </summary>
    [Fact]
    public void The_settings_pages_no_longer_carry_a_collapsible_group()
    {
        var markup = ViewSource.MarkupAnd("SettingsSystemView.axaml",
            "SettingsTriggersView.axaml", "SettingsAppearanceView.axaml");

        Assert.DoesNotContain("<fa:FASettingsExpander ", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("IsExpanded", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// 分组标题是读的，不是按的：标题区里没有按钮，也不在 Tab 顺序上，空格与回车都不改变内容。
    /// </summary>
    [Fact]
    public void A_group_heading_is_not_a_focus_stop()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var groups = window.GetVisualDescendants()
                .OfType<SettingsGroup>()
                .Where(group => group.IsEffectivelyVisible)
                .ToList();
            Assert.NotEmpty(groups);

            foreach (var group in groups)
            {
                var header = Assert.IsAssignableFrom<Control>(group.Header);
                Assert.False(header.Focusable);
                Assert.Empty(header.GetVisualDescendants().OfType<Button>());

                var rows = group.GetVisualDescendants().OfType<FASettingsExpanderItem>().ToList();
                Assert.NotEmpty(rows);
                header.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Space,
                });
                header.RaiseEvent(new KeyEventArgs
                {
                    RoutedEvent = InputElement.KeyDownEvent,
                    Key = Key.Enter,
                });

                Assert.All(rows, row => Assert.True(row.IsEffectivelyVisible));
            }

            window.Close();
        });
    }

    /// <summary>
    /// 竖向滚动只属于内容列：组里不再套一层自己的滚动，页面上任何时候都只有那一个。
    /// </summary>
    [Fact]
    public void Only_the_content_column_scrolls()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);

            foreach (var page in Pages)
            {
                viewModel.SetPage(page);
                Dispatcher.UIThread.RunJobs();

                var scrollers = window.GetVisualDescendants()
                    .OfType<ScrollViewer>()
                    .Where(scroll => scroll != Scroller(window)
                        && scroll.IsEffectivelyVisible
                        && scroll.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
                        && scroll.Content is Control content
                        && (content is SettingsGroup
                            || content.GetVisualDescendants().OfType<SettingsGroup>().Any()))
                    .ToList();
                Assert.Empty(scrollers);
                Assert.True(Scroller(window).IsEffectivelyVisible);
            }

            window.Close();
        });
    }

    /// <summary>
    /// 四十行仍是原来那些行：名字不变，行里的主体控件类型不变，代码后置按名字找的控件也在。
    /// </summary>
    [Fact]
    public void Every_row_keeps_its_name_and_control()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);

            var rows = new List<FASettingsExpanderItem>();
            foreach (var page in Pages)
            {
                viewModel.SetPage(page);
                Dispatcher.UIThread.RunJobs();
                rows.AddRange(Rows(window));
            }

            Assert.Equal(40, rows.Count);

            var bodies = new Dictionary<string, Type>
            {
                ["LoadLastProjectCheck"] = typeof(StackPanel),
                ["SystemAtStartCheck"] = typeof(StackPanel),
                ["EnabledFirstCheck"] = typeof(StackPanel),
                ["LogCountBox"] = typeof(StackPanel),
                ["SoundSystemCheck"] = typeof(TextBlock),
                ["SoundMacroCheck"] = typeof(TextBlock),
                ["FrameRateFollowCheck"] = typeof(StackPanel),
                ["FrameCacheCheck"] = typeof(StackPanel),
                ["FrameCacheAgeRow"] = typeof(StackPanel),
                ["InputLimitBox"] = typeof(StackPanel),
                ["IgnoreOwnInputCheck"] = typeof(StackPanel),
                ["DriverAttachCheck"] = typeof(StackPanel),
                ["RunAsAdminCheck"] = typeof(StackPanel),
            };

            foreach (var (name, body) in bodies)
            {
                var row = Assert.Single(rows, candidate => candidate.Name == name);
                Assert.IsType(body, row.Content);
            }

            Assert.NotNull(SettingsView.Find<FANumberBox>(window, "FrameRateBox"));
            Assert.NotNull(SettingsView.Find<ToggleSwitch>(window, "PreloadOcrCheck"));
            Assert.NotNull(SettingsView.Find<FASettingsExpanderItem>(window, "RunAsAdminCheck"));
            window.Close();
        });
    }

    /// <summary>
    /// 换页签只改可见性：页视图、内容列与那个滚动区都是同一份，回到原页时位置没有被重建归零。
    /// </summary>
    [Fact]
    public void The_tab_keeps_its_scroll_position()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Dispatcher.UIThread.RunJobs();

            var page = Assert.Single(window.GetVisualDescendants().OfType<SettingsSystemView>());
            var scroll = Scroller(window);
            scroll.Offset = new Vector(0, 30);
            Dispatcher.UIThread.RunJobs();
            var placed = scroll.Offset.Y;
            Assert.True(placed > 0, "the page is shorter than the window; nothing to keep");

            viewModel.SetPage(SettingsSubPage.Screen);
            Dispatcher.UIThread.RunJobs();
            viewModel.SetPage(SettingsSubPage.General);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(page, Assert.Single(window.GetVisualDescendants().OfType<SettingsSystemView>()));
            Assert.Same(scroll, Scroller(window));
            Assert.Equal(placed, scroll.Offset.Y);
            window.Close();
        });
    }

    /// <summary>
    /// 一行被隐藏时只少那一行：组标题与其余行仍在，组的高度只缩掉该行的部分，不整组塌掉。
    /// </summary>
    [Fact]
    public void A_hidden_row_does_not_collapse_the_group()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var group = window.GetVisualDescendants()
                .OfType<SettingsGroup>()
                .First(candidate => candidate.IsEffectivelyVisible);
            var rows = group.GetVisualDescendants().OfType<FASettingsExpanderItem>().ToList();
            var hidden = Assert.Single(rows, row => row.Name == "LoadLastProjectCheck");
            var height = group.Bounds.Height;

            hidden.IsVisible = false;
            Dispatcher.UIThread.RunJobs();

            Assert.True(Assert.IsAssignableFrom<Control>(group.Header).IsEffectivelyVisible);
            Assert.All(rows.Where(row => row != hidden), row => Assert.True(row.IsEffectivelyVisible));
            Assert.True(group.Bounds.Height < height);
            Assert.True(group.Bounds.Height >= height - hidden.Bounds.Height - 1);
            window.Close();
        });
    }

    private static (int Groups, int Rows) Count(SettingsWindow window)
    {
        var groups = window.GetVisualDescendants()
            .OfType<SettingsGroup>()
            .Where(group => group.IsEffectivelyVisible)
            .ToList();
        return (groups.Count, groups.Sum(group =>
            group.GetVisualDescendants().OfType<FASettingsExpanderItem>().Count()));
    }

    private static IEnumerable<FASettingsExpanderItem> Rows(SettingsWindow window)
        => window.GetVisualDescendants()
            .OfType<SettingsGroup>()
            .Where(group => group.IsEffectivelyVisible)
            .SelectMany(group => group.GetVisualDescendants().OfType<FASettingsExpanderItem>());

    private static ScrollViewer Scroller(SettingsWindow window)
        => window.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(scroll => scroll.Content is Panel { Name: "SettingsPanel" });

    /// <summary>卡片标题是一行标题加一行小字，读文本时两段都算进来。</summary>
    private static string HeaderText(SettingsGroup group)
        => group.Header switch
        {
            string text => text,
            StackPanel panel => string.Join(
                " ", panel.Children.OfType<TextBlock>().Select(block => block.Text)),
            _ => string.Empty,
        };

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

            var cap = Assert.IsType<FANumberBox>(SettingsView.Find<FANumberBox>(window, "FrameRateBox"));
            Assert.Equal(viewModel.FastestRate, (int)cap.Maximum);
            Assert.True(viewModel.FrameRateFollow);
            Assert.False(viewModel.FrameRateEnabled);

            viewModel.FrameCache = true;
            Assert.True(viewModel.FrameCacheAgeEnabled);

            window.Close();
        });
    }
}
