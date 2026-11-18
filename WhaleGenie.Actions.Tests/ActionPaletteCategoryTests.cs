using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class ActionPaletteCategoryTests
{
    [Fact]
    public void Basic_categories_keep_the_old_order_without_flow_or_conditions()
    {
        var palette = new ActionPaletteViewModel();

        Assert.Equal(
        [
            null,
            ActionCategory.Input,
            ActionCategory.Vision,
            ActionCategory.Uia,
            ActionCategory.Browser,
            ActionCategory.Script,
            ActionCategory.Process,
            ActionCategory.Data,
            ActionCategory.File,
            ActionCategory.Clipboard,
            ActionCategory.System,
        ], palette.Categories.Select(category => category.Value));
        Assert.Null(palette.SelectedCategory!.Value);
    }

    /// <summary>
    /// The submacro dock lists what the document has, not only what it already calls: a submacro
    /// that was just made and saved is exactly the one somebody wants to call next.
    /// </summary>
    [Fact]
    public void The_submacro_dock_lists_the_submacros_the_document_has()
    {
        var palette = new ActionPaletteViewModel();
        var sub = new MacroItem { Name = "收尾", Description = "收个尾" };

        palette.SetSources(null, [sub], []);
        palette.SelectDock(ActionPaletteDock.SubMacros);

        var card = Assert.Single(palette.Cards);
        Assert.Equal("收尾", card.Title);
        Assert.Equal("收个尾", card.Description);
        Assert.False(palette.HasNoCards);

        // With none at all, the dock says what to do about it rather than looking broken.
        palette.SetSources(null, [], []);
        Assert.True(palette.HasNoCards);
        Assert.Equal(Strings.Get("Palette.NoSubMacros"), palette.EmptyText);
    }

    [Fact]
    public void Choosing_a_category_filters_actions_and_keeps_the_choice_across_docks()
    {
        var palette = new ActionPaletteViewModel();
        var input = palette.Categories.Single(category =>
            category.Value == ActionCategory.Input);

        palette.SelectedCategory = input;

        Assert.NotEmpty(palette.Cards);
        Assert.All(palette.Cards, card =>
        {
            Assert.Equal(ActionCategory.Input,
                ActionPaletteCategory(ActionCatalog.Find(card.Key)!.Category));
            Assert.Equal(Strings.Get("Add.Category.Input"), card.CategoryLabel);
            Assert.True(card.HasCategoryLabel);
            Assert.IsType<SolidColorBrush>(card.Accent);
            Assert.False(string.IsNullOrWhiteSpace(card.Description));
        });

        palette.SelectDock(ActionPaletteDock.Flow);
        palette.SelectDock(ActionPaletteDock.Basic);
        Assert.Same(input, palette.SelectedCategory);
    }

    /// <summary>
    /// 搜索不分「基础动作」与「流程控制」两个页签：人问「延迟」，不该先知道它归哪一栏。页签
    /// 仍然管浏览，清空搜索就回到原来那一页。
    /// </summary>
    [Fact]
    public void Searching_finds_actions_from_both_docks()
    {
        Ui.Run(() =>
        {
            var palette = new ActionPaletteViewModel();
            palette.SelectDock(ActionPaletteDock.Basic);

            palette.Search = "延迟";
            Assert.Contains(palette.Cards, card => card.Key == "control.delay");

            palette.Search = "点击";
            Assert.Contains(palette.Cards, card => card.Key == "uia.click");
            Assert.Contains(palette.Cards, card => card.Key == "browser.click");

            // 清空搜索仍是原来那一页：流程动作不在基础动作里。
            palette.Search = string.Empty;
            Assert.DoesNotContain(palette.Cards, card => card.Key == "control.delay");
        });
    }

    /// <summary>
    /// 按老说法问「点击元素」，两张卡片都要找得到：动作名现在分开叫「点击界面元素」与「点击网页
    /// 元素」，整句已经不在名字里，按两个字一段拆开仍在。分开叫是为了让人一眼看出哪张点的是网页，
    /// 哪张点的是桌面上的控件。
    /// </summary>
    [Fact]
    public void Searching_an_old_name_finds_both_actions_that_now_say_which_family_they_are()
    {
        Ui.Run(() =>
        {
            var palette = new ActionPaletteViewModel();
            palette.Search = "点击元素";

            Assert.Contains(palette.Cards, card => card.Key == "uia.click");
            Assert.Contains(palette.Cards, card => card.Key == "browser.click");

            var names = palette.Cards
                .Where(card => card.Key is "uia.click" or "browser.click")
                .Select(card => card.Title)
                .ToList();
            Assert.Equal(2, names.Distinct().Count());
        });
    }

    [Fact]
    public void Search_stays_inside_the_selected_category()
    {
        var palette = new ActionPaletteViewModel();
        palette.SelectedCategory = palette.Categories.Single(category =>
            category.Value == ActionCategory.Input);

        palette.Search = "findImage";
        Assert.Empty(palette.Cards);

        palette.Search = "keyPress";
        Assert.Contains(palette.Cards, card => card.Key == "input.keyPress");
        Assert.All(palette.Cards, card => Assert.Equal(ActionCategory.Input,
            ActionPaletteCategory(ActionCatalog.Find(card.Key)!.Category)));
    }

    [Fact]
    public void Ocr_and_window_actions_join_their_reader_categories()
    {
        var palette = new ActionPaletteViewModel();
        var visionKey = ActionCatalog.RunnableActions
            .First(action => action.Category == ActionCategory.Ocr).Key;
        var windowKey = ActionCatalog.RunnableActions
            .First(action => action.Category == ActionCategory.Window).Key;

        palette.SelectedCategory = palette.Categories.Single(category =>
            category.Value == ActionCategory.Vision);
        Assert.Contains(palette.Cards, card => card.Key == visionKey);

        palette.SelectedCategory = palette.Categories.Single(category =>
            category.Value == ActionCategory.Process);
        Assert.Contains(palette.Cards, card => card.Key == windowKey);
    }

    /// <summary>
    /// Every action the basic dock can show has to name one of the small groups that category is cut
    /// into, and the name has to be one the palette's order table knows. Without this, a new action
    /// silently lands in a section called "Other" — or, worse, in a section nobody ordered.
    /// </summary>
    [Fact]
    public void Every_basic_action_names_a_palette_group_and_the_order_table_knows_it()
    {
        var basic = ActionCatalog.RunnableActions
            .Where(action => action.Category != ActionCategory.Control)
            .ToList();
        Assert.NotEmpty(basic);

        var ungrouped = basic.Where(action => action.Group.Length == 0)
            .Select(action => action.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(ungrouped.Count == 0,
            "these actions name no palette group: " + string.Join(", ", ungrouped));

        var unknown = basic
            .Where(action => !ActionCategoryPresentation
                .BasicGroups(ActionPaletteCategory(action.Category))
                .Contains(action.Group, StringComparer.Ordinal))
            .Select(action => $"{action.Key} → {action.Group}")
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(unknown.Count == 0,
            "these groups are missing from the palette's order table: " + string.Join(", ", unknown));
    }

    /// <summary>
    /// A category with many actions is cut into the same kind of small sections the control-flow
    /// dock has: named, ordered, and with every card in exactly one of them. The titles come from
    /// the string table, so a missing translation shows up as a raw key rather than an empty line.
    /// </summary>
    [Fact]
    public void Every_basic_category_is_cut_into_its_small_groups_in_order()
    {
        var categories = new[]
        {
            ActionCategory.Input, ActionCategory.Vision, ActionCategory.Uia,
            ActionCategory.Browser, ActionCategory.Script, ActionCategory.Process,
            ActionCategory.Data, ActionCategory.File, ActionCategory.Clipboard,
            ActionCategory.System,
        };

        foreach (var category in categories)
        {
            var palette = new ActionPaletteViewModel();
            palette.SelectedCategory = palette.Categories.Single(choice => choice.Value == category);

            var expected = ActionCategoryPresentation.BasicGroups(category)
                .Select(id => Strings.Get($"Palette.Group.{id}"))
                .ToList();
            var titles = palette.Sections.Select(section => section.Title).ToList();

            Assert.Equal(expected, titles);
            Assert.All(titles, title => Assert.DoesNotContain("Palette.Group.", title));
            Assert.All(palette.Sections, section => Assert.True(section.HasTitle));
            Assert.DoesNotContain(Strings.Get("Palette.Group.Other"), titles);
            Assert.Equal(palette.Cards.Count, palette.Sections.Sum(section => section.Cards.Count));
        }
    }

    /// <summary>
    /// "All" stays one untitled list: it is the browse-everything view, and repeating the rail's
    /// category names as headings over 150 cards only pushes them further down.
    /// </summary>
    [Fact]
    public void The_all_view_stays_one_untitled_list()
    {
        var palette = new ActionPaletteViewModel();

        Assert.Single(palette.Sections);
        Assert.False(palette.Sections[0].HasTitle);
        Assert.Equal(palette.Cards.Count, palette.Sections[0].Cards.Count);
    }

    /// <summary>
    /// The sections are not just a model detail: the palette draws their titles as text, between the
    /// cards. This walks the real view once, on the category the user named first.
    /// </summary>
    [Fact]
    public void The_palette_draws_the_small_group_titles()
    {
        Ui.Run(() =>
        {
            var palette = new ActionPaletteViewModel();
            var view = new ActionPaletteView { DataContext = palette };
            var window = new Window { Width = 280, Height = 600, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            palette.SelectedCategory = palette.Categories.Single(choice =>
                choice.Value == ActionCategory.Input);
            Dispatcher.UIThread.RunJobs();

            var shown = view.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var id in ActionCategoryPresentation.BasicGroups(ActionCategory.Input))
            {
                Assert.Contains(Strings.Get($"Palette.Group.{id}"), shown);
            }

            window.Close();
        });
    }

    /// <summary>
    /// A submacro that is part of the document shows up in the dock as soon as the editor opens,
    /// and a run started from the editor can reach it — the two halves that were missing between
    /// "the file has a submacro" and "a step can call it".
    /// </summary>
    [Fact]
    public void A_submacro_in_the_document_shows_in_the_dock_and_runs()
    {
        Ui.Run(() =>
        {
            var sub = new MacroItem { Name = "收尾" };
            var macro = new MacroItem { Name = "外面" };
            macro.SubMacros.Add(sub);

            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;
            var model = (ActionPaletteViewModel)palette.DataContext!;
            model.SelectDock(ActionPaletteDock.SubMacros);
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(model.Cards, card => card.Title == "收尾");
            Assert.NotNull(window.BuildLibrary().Steps("收尾"));

            window.Close();
        });
    }

    [Fact]
    public void The_category_rail_belongs_only_to_the_basic_dock()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;
            var model = (ActionPaletteViewModel)palette.DataContext!;
            var rail = palette.FindControl<Border>("BasicCategoryDock");

            Assert.NotNull(rail);
            Assert.True(rail!.IsVisible);
            var buttons = rail.GetVisualDescendants().OfType<Button>()
                // 分类栏那颗方钮：原来是窗口本地的 `Category`，现在并进库里的 `Button.Rail`。
                .Where(button => button.Classes.Contains("Rail"))
                .ToList();
            Assert.Equal(model.Categories.Count, buttons.Count);
            var allButton = buttons.First(button =>
                button.DataContext is ActionCategoryChoice { Value: null });
            Assert.Equal(Strings.Get("Add.AllActions"), ToolTip.GetTip(allButton));

            model.SelectDock(ActionPaletteDock.Flow);
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsBasicDock);
            Assert.False(rail.IsEffectivelyVisible);

            model.SelectDock(ActionPaletteDock.Basic);
            Dispatcher.UIThread.RunJobs();
            Assert.True(rail.IsEffectivelyVisible);
            window.Close();
        });
    }

    [Fact]
    public void The_flow_dock_lists_every_visible_control_action_once()
    {
        var palette = new ActionPaletteViewModel();
        palette.SelectDock(ActionPaletteDock.Flow);

        var expected = ActionCatalog.RunnableActions
            .Where(action => action.Category == ActionCategory.Control)
            .Select(action => action.Key)
            .Order()
            .ToList();
        var shown = palette.Sections.SelectMany(section => section.Cards)
            .Select(card => card.Key)
            .Order()
            .ToList();

        Assert.Equal(expected, shown);
        Assert.Equal(
            [
                Strings.Get("Palette.Flow.Branch"),
                Strings.Get("Palette.Flow.Jump"),
                Strings.Get("Palette.Flow.Waiting"),
                Strings.Get("Palette.Flow.Data"),
                Strings.Get("Palette.Flow.Notes"),
            ],
            palette.Sections.Select(section => section.Title));
        Assert.DoesNotContain("control.case", shown);
        Assert.DoesNotContain("control.sequence", shown);
    }

    [Fact]
    public void Actions_are_the_original_compact_wrapping_buttons()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var palette = window.FindControl<ActionPaletteView>("ActionPalette")!;

            var button = palette.GetVisualDescendants().OfType<Button>()
                .First(candidate => candidate.DataContext is ActionPaletteCardViewModel
                    && candidate.Classes.Contains("Secondary"));
            Assert.Equal(30, button.Height);
            Assert.NotNull(button.GetVisualAncestors().OfType<WrapPanel>().FirstOrDefault());
            Assert.False(string.IsNullOrWhiteSpace(ToolTip.GetTip(button) as string));

            var point = button.TranslatePoint(
                new Avalonia.Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
                ?? default;
            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            Assert.True(palette.HasArmedDrag);
            window.MouseUp(point, MouseButton.Left);
            window.Close();
        });
    }

    private static ActionCategory ActionPaletteCategory(ActionCategory category)
        => ActionCategoryPresentation.PickerCategory(category);
}
