using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MenuWindowTests
{
    [Fact]
    public void The_menu_is_a_compact_four_by_six_grid()
    {
        Ui.Run(() =>
        {
            var layout = Layout();
            var window = new MacroMenuWindow(layout, Actions());
            window.Show();

            Assert.Equal(350, window.Width);
            Assert.Equal(630, window.Height);
            Assert.False(window.ShowActivated);
            Assert.Equal(4, ProjectTriggers.MenuColumns);
            Assert.Equal(6, ProjectTriggers.MenuRows);
            Assert.Equal(24, window.GetVisualDescendants()
                .OfType<Button>()
                .Count(button => button.Classes.Contains("MenuCell")));
            Assert.All(window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("MenuCell")),
                button => Assert.Equal(80, button.Width));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == Strings.Get("Main.Title"));
            window.Close();
        });
    }

    [Fact]
    public void A_built_in_icon_replaces_the_cell_label()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "one" };
            var entry = new TriggerEntry
            {
                Kind = TriggerKind.Menu,
                MacroName = macro.Name,
                MenuPageId = "common",
                MenuRow = 0,
                MenuColumn = 0,
                MenuIcon = new MenuIconReference(MenuIconKind.Builtin, "play"),
            };
            var layout = new MenuRuntimeLayout(
            [
                new MenuRuntimePage("common", false, string.Empty,
                [
                    new MenuRuntimeCell(0, 0, "Hidden label", entry.MenuIcon,
                        entry, macro),
                ]),
            ],
            [new MenuRuntimePage("default", true, "Default", [])]);
            var window = new MacroMenuWindow(layout, Actions());
            window.Show();

            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Hidden label");
            Assert.Contains(window.GetVisualDescendants().OfType<IconView>(),
                icon => icon.Data == AppIcons.Play);
            window.Close();
        });
    }

    [Fact]
    public void A_cell_click_runs_the_assigned_macro()
    {
        Ui.RunAsync(async () =>
        {
            var layout = Layout();
            var window = new MacroMenuWindow(layout, Actions());
            window.Show();

            var button = window.GetVisualDescendants().OfType<Button>().First(candidate =>
                candidate.Classes.Contains("MenuCell")
                && candidate.Tag is MenuSlotRef { Cell: not null });
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

            Assert.Equal("one", (await window.Answer)?.Name);
            return true;
        });
    }

    [Fact]
    public void Editing_can_add_another_common_page_and_is_limited_to_eight()
    {
        Ui.RunAsync(async () =>
        {
            var layout = Layout();
            var window = new MacroMenuWindow(layout, Actions());
            window.Show();
            window.SetEditing(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, window.CommonPageCount);
            Assert.Equal(23, window.GetVisualDescendants().OfType<IconView>()
                .Count(icon => icon.Data == AppIcons.SelectionPlus));
            window.Close();
            return true;
        });
    }

    [Fact]
    public void Arrow_navigation_skips_empty_cells()
    {
        Ui.Run(() =>
        {
            var first = new MacroItem { Name = "first" };
            var second = new MacroItem { Name = "second" };
            var page = new MenuRuntimePage("common", false, string.Empty,
            [
                new MenuRuntimeCell(0, 0, "First", null, Menu(first), first),
                new MenuRuntimeCell(0, 3, "Second", null, Menu(second), second),
            ]);
            var layout = new MenuRuntimeLayout([page],
                [new MenuRuntimePage("default", true, "Default", [])]);
            var window = new MacroMenuWindow(layout, Actions());
            window.Show();

            window.HandleKey("Right");
            Assert.Equal("first", window.SelectedCell?.Macro.Name);
            window.HandleKey("Right");
            Assert.Equal("second", window.SelectedCell?.Macro.Name);
            window.Close();
        });
    }

    [Fact]
    public void Search_scans_all_common_pages_and_keeps_cells_in_place()
    {
        Ui.Run(() =>
        {
            var first = new MacroItem { Name = "First" };
            var second = new MacroItem { Name = "Second" };
            var commonOne = new MenuRuntimePage("common-1", false, string.Empty,
            [
                new MenuRuntimeCell(0, 0, "First", null, Menu(first), first),
            ]);
            var commonTwo = new MenuRuntimePage("common-2", false, string.Empty,
            [
                new MenuRuntimeCell(2, 3, "Second", null, Menu(second), second),
            ]);
            var layout = new MenuRuntimeLayout([commonOne, commonTwo],
                [new MenuRuntimePage("default", true, "Default", [])]);
            var window = new MacroMenuWindow(layout, Actions());
            window.Show();

            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());
            search.Text = "Second";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, window.CommonPage);
            window.HandleKey("Down");
            Assert.Equal("Second", window.SelectedCell?.Macro.Name);
            window.Close();
        });
    }

    [Fact]
    public void Top_and_bottom_page_dots_are_rendered_separately()
    {
        Ui.Run(() =>
        {
            var common = new MenuRuntimePage("common", false, string.Empty, []);
            var contextOne = new MenuRuntimePage("context-1", false, "Rule", []);
            var contextTwo = new MenuRuntimePage("context-2", false, "Rule", []);
            var layout = new MenuRuntimeLayout([common],
                [contextOne, contextTwo]);
            var window = new MacroMenuWindow(layout, Actions());
            window.Show();

            Assert.Equal(3, window.GetVisualDescendants().OfType<Ellipse>().Count());
            Assert.Equal(2, window.ContextPageCount);
            window.Close();
        });
    }

    [Fact]
    public void Search_includes_the_current_window_rule_region()
    {
        Ui.Run(() =>
        {
            var common = new MenuRuntimePage("common", false, string.Empty, []);
            var contextMacro = new MacroItem { Name = "ContextOnly" };
            var context = new MenuRuntimePage("context", false, "Rule",
            [
                new MenuRuntimeCell(1, 1, "ContextOnly", null,
                    Menu(contextMacro), contextMacro),
            ]);
            var window = new MacroMenuWindow(
                new MenuRuntimeLayout([common], [context]), Actions());
            window.Show();
            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());

            search.Text = "ContextOnly";
            Dispatcher.UIThread.RunJobs();
            window.HandleKey("Down");

            Assert.Equal("ContextOnly", window.SelectedCell?.Macro.Name);
            window.Close();
        });
    }

    [Fact]
    public void Focusing_search_temporarily_activates_the_menu()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions());
            window.Show();
            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());

            search.Focus();
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsSearchFocused);
            Assert.True(window.ShowActivated);
            window.Close();
        });
    }

    [Fact]
    public void Closing_from_an_outside_click_does_not_steal_focus_back()
    {
        Ui.RunAsync(async () =>
        {
            var restores = 0;
            var window = new MacroMenuWindow(Layout(), Actions(() => restores++));
            window.Show();
            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());
            search.Focus();
            Dispatcher.UIThread.RunJobs();

            window.CloseFromOutside();
            Assert.Null(await window.Answer);
            Assert.Equal(0, restores);
            return true;
        });
    }

    [Fact]
    public void Menu_position_is_near_the_pointer_and_clamped_to_the_work_area()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions());

            Assert.Equal(new Avalonia.PixelPoint(512, 412),
                window.PlaceNear(new Avalonia.PixelPoint(500, 400),
                    new Avalonia.PixelRect(0, 0, 1000, 1200), 1));
            Assert.Equal(new Avalonia.PixelPoint(618, 58),
                window.PlaceNear(new Avalonia.PixelPoint(980, 700),
                    new Avalonia.PixelRect(0, 0, 1000, 800), 1));
            Assert.Equal(new Avalonia.PixelPoint(-1888, 112),
                window.PlaceNear(new Avalonia.PixelPoint(-1900, 100),
                    new Avalonia.PixelRect(-1920, 0, 1920, 1080), 1));
        });
    }

    private static MenuRuntimeLayout Layout()
    {
        var macro = new MacroItem { Name = "one" };
        var entry = new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = macro.Name,
            MenuPageId = "common",
            MenuRow = 0,
            MenuColumn = 0,
        };
        var cell = new MenuRuntimeCell(0, 0, "One", null, entry, macro);
        return new MenuRuntimeLayout(
        [
            new MenuRuntimePage("common", false, string.Empty, [cell]),
        ],
        [new MenuRuntimePage("default", true, "Default", [])]);
    }

    private static TriggerEntry Menu(MacroItem macro) => new()
    {
        Kind = TriggerKind.Menu,
        MacroName = macro.Name,
    };

    private static MenuWindowActions Actions(Action? restore = null)
        => new(
            _ => { },
            () => { },
            restore ?? (() => { }),
            () => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            (_, _) => Task.FromResult<MenuRuntimeLayout?>(null),
            (_, _) => Task.FromResult<MenuRuntimeLayout?>(null));
}
