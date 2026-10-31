using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MenuWindowTests
{
    [Fact]
    public void The_macro_picker_shows_each_macro_by_name()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "整理报表" };
            var window = new MenuMacroPickerWindow([macro], enabledOnly: false);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var texts = window.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text)
                .ToList();
            Assert.Contains("整理报表", texts);
            Assert.DoesNotContain(texts, text =>
                text is not null && text.Contains("WhaleGenie.Models.MacroItem", StringComparison.Ordinal));

            window.Close();
        });
    }

    [Fact]
    public void The_menu_is_a_compact_four_by_six_grid_with_closed_search()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();

            Assert.Equal(350, window.Width);
            Assert.Equal(667, window.Height);
            Assert.False(window.ShowActivated);
            Assert.False(window.IsSearchOpen);
            Assert.False(window.IsPinned);
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
            Assert.NotNull(FindButton(window, "MenuSearchButton"));
            Assert.NotNull(FindButton(window, "MenuPinButton"));
            Assert.NotNull(FindButton(window, "MenuCloseButton"));
            Assert.NotNull(FindButton(window, "MenuAddWindowRule"));
            Assert.NotNull(FindButton(window, "MenuBlockWindow"));
            window.Close();
        });
    }

    [Fact]
    public void The_title_bar_starts_near_the_top_and_buttons_use_their_own_cursor()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var title = window.GetVisualDescendants().OfType<Grid>()
                .Single(grid => grid.Name == "MenuTitleBar");

            Assert.Equal(40, title.Bounds.Height);
            Assert.True((title.TranslatePoint(default, window)?.Y ?? double.MaxValue) <= 5);
            Assert.Same(MacroMenuWindow.TitleCursorForCheck, title.Cursor);
            foreach (var name in new[]
                     {
                         "MenuSettingsButton", "MenuSearchButton",
                         "MenuPinButton", "MenuCloseButton",
                     })
            {
                Assert.Same(MacroMenuWindow.ButtonCursorForCheck,
                    FindButton(window, name).Cursor);
            }
            window.Close();
        });
    }

    [Fact]
    public void The_collapsed_search_cannot_take_focus_until_its_button_is_clicked()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());

            search.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsSearchFocused);

            var button = FindButton(window, "MenuSearchButton");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.IsSearchOpen);
            Assert.True(window.IsSearchFocused);
            Assert.True(window.ShowActivated);
            Assert.Equal(667, window.Height);
            window.Close();
        });
    }

    [Fact]
    public void Clicking_elsewhere_in_the_menu_hides_search_after_the_click_finishes()
    {
        Ui.Run(() =>
        {
            var added = 0;
            var window = new MacroMenuWindow(Layout(),
                Actions(addRule: () =>
                {
                    added++;
                    return Task.FromResult<MenuRuntimeLayout?>(null);
                }), "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.SetSearchOpen(true);
            Dispatcher.UIThread.RunJobs();
            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());
            search.Text = "one";
            var addRule = FindButton(window, "MenuAddWindowRule");
            var point = Waypoint(window, addRule);

            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, added);
            Assert.False(window.IsSearchOpen);
            Assert.Equal(string.Empty, search.Text);
            Assert.True(window.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public void The_search_button_still_collapses_an_open_search()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            window.SetSearchOpen(true);
            Dispatcher.UIThread.RunJobs();

            FindButton(window, "MenuSearchButton")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.False(window.IsSearchOpen);
            Assert.True(window.IsVisible);
            window.Close();
        });
    }

    [Fact]
    public void Escape_clears_then_collapses_search_then_closes_the_menu()
    {
        Ui.RunAsync(async () =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            window.SetSearchOpen(true);
            Dispatcher.UIThread.RunJobs();
            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());
            search.Text = "one";

            window.HandleKey("Esc");
            Assert.Equal(string.Empty, search.Text);
            Assert.True(window.IsSearchOpen);

            window.HandleKey("Esc");
            Assert.False(window.IsSearchOpen);
            Assert.True(window.IsVisible);

            window.HandleKey("Esc");
            Assert.Null(await window.Answer);
            return true;
        });
    }

    [Fact]
    public void A_pinned_menu_ignores_outside_clicks_and_global_escape()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();

            window.SetPinned(true);
            window.CloseFromOutside();
            window.HandleKey("Esc");

            Assert.True(window.IsPinned);
            Assert.True(window.IsVisible);
            Assert.Equal(AppIcons.PinOff,
                ((IconView)FindButton(window, "MenuPinButton").Content!).Data);
            window.CloseFromLauncher();
        });
    }

    [Fact]
    public void The_footer_actions_are_disabled_without_a_usable_foreground()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions());
            window.Show();

            Assert.False(FindButton(window, "MenuAddWindowRule").IsEnabled);
            Assert.False(FindButton(window, "MenuBlockWindow").IsEnabled);
            Assert.Equal(Strings.Get("Menu.NoForeground"),
                ToolTip.GetTip(FindButton(window, "MenuAddWindowRule")));
            window.Close();
        });
    }

    [Fact]
    public void The_footer_adds_a_rule_and_the_block_button_closes_the_menu()
    {
        Ui.RunAsync(async () =>
        {
            var added = 0;
            var window = new MacroMenuWindow(Layout(),
                Actions(addRule: () =>
                {
                    added++;
                    return Task.FromResult<MenuRuntimeLayout?>(null);
                }, block: () => Task.FromResult(true)),
                "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            FindButton(window, "MenuAddWindowRule")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, added);

            FindButton(window, "MenuBlockWindow")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Null(await window.Answer);
            Assert.False(window.IsVisible);
            return true;
        });
    }

    [Fact]
    public void An_occupied_cell_has_the_four_direct_actions_and_a_macro_tooltip()
    {
        Ui.Run(() =>
        {
            var layout = Layout(description: "Opens the useful thing.");
            var window = new MacroMenuWindow(layout, Actions(), "notepad");
            window.Show();
            var button = OccupiedCell(window);

            Assert.Equal("one\nOpens the useful thing.", ToolTip.GetTip(button));
            Assert.Equal(
                [
                    Strings.Get("Menu.EditMacro"),
                    Strings.Get("Menu.Replace"),
                    Strings.Get("Menu.ChooseIcon"),
                    Strings.Get("Menu.Debug"),
                ],
                button.ContextMenu!.Items.OfType<MenuItem>()
                    .Select(item => item.Header?.ToString() ?? string.Empty).ToArray());
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(),
                candidate => candidate.Name == "MenuEditButton");
            window.Close();
        });
    }

    [Fact]
    public void Right_clicking_a_cell_opens_its_context_menu_and_reports_the_popup()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cell = OccupiedCell(window);
            var point = Waypoint(window, cell);

            window.MouseDown(point, MouseButton.Right, RawInputModifiers.RightMouseButton);
            window.MouseUp(point, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();

            Assert.True(cell.ContextMenu!.IsOpen);
            Assert.True(window.HasOpenPopup);
            Assert.True(window.IsVisible);
            cell.ContextMenu.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.HasOpenPopup);
            window.Close();
        });
    }

    [Fact]
    public void Edit_macro_closes_the_menu_before_opening_the_editor()
    {
        Ui.RunAsync(async () =>
        {
            MacroItem? edited = null;
            var window = new MacroMenuWindow(Layout(),
                Actions(edit: macro => edited = macro), "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var item = OccupiedCell(window).ContextMenu!.Items.OfType<MenuItem>().First();
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal("one", edited?.Name);
            Assert.Null(await window.Answer);
            Assert.False(window.IsVisible);
            return true;
        });
    }

    [Fact]
    public void A_built_in_icon_replaces_the_cell_label()
    {
        Ui.Run(() =>
        {
            var layout = Layout(icon: new MenuIconReference(MenuIconKind.Builtin, "Play"));
            var window = new MacroMenuWindow(layout, Actions(), "notepad");
            window.Show();

            Assert.Contains(window.GetVisualDescendants().OfType<IconView>(),
                icon => icon.Data == AppIcons.Play);
            Assert.DoesNotContain(OccupiedCell(window).GetVisualDescendants()
                    .OfType<TextBlock>(),
                text => text.Text == "one");
            window.Close();
        });
    }

    [Fact]
    public void A_cell_click_runs_the_assigned_macro()
    {
        Ui.RunAsync(async () =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();

            OccupiedCell(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal("one", (await window.Answer)?.Name);
            return true;
        });
    }

    [Fact]
    public void A_short_mouse_click_runs_without_starting_a_drag()
    {
        Ui.RunAsync(async () =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cell = OccupiedCell(window);
            var point = Waypoint(window, cell);

            window.MouseDown(point, MouseButton.Left, RawInputModifiers.LeftMouseButton);
            await Task.Delay(80);
            window.MouseUp(point, MouseButton.Left);

            Assert.Equal("one", (await window.Answer)?.Name);
            return true;
        });
    }

    [Fact]
    public void A_long_press_drag_moves_a_cell_without_running_it()
    {
        Ui.RunAsync(async () =>
        {
            MenuSlotRef? source = null;
            MenuSlotRef? target = null;
            var first = new MacroItem { Name = "first" };
            var second = new MacroItem { Name = "second" };
            var page = new MenuRuntimePage("common", false, string.Empty,
            [
                Cell(0, 0, first),
                Cell(0, 1, second),
            ]);
            var layout = new MenuRuntimeLayout([page],
                [new MenuRuntimePage("default", true, "Default", [])]);
            var window = new MacroMenuWindow(layout, Actions(move: (from, to) =>
            {
                source = from;
                target = to;
                return Task.FromResult<MenuRuntimeLayout?>(null);
            }), "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cells = window.GetVisualDescendants().OfType<Button>()
                .Where(candidate => candidate.Classes.Contains("MenuCell")
                    && candidate.Tag is MenuSlotRef { Cell: not null })
                .OrderBy(candidate => ((MenuSlotRef)candidate.Tag!).Column)
                .ToList();
            var from = Waypoint(window, cells[0]);
            var to = Waypoint(window, cells[1]);

            Assert.Equal(TimeSpan.FromMilliseconds(400), MacroMenuWindow.CellDragDelay);
            window.BeginCellDragForCheck((MenuSlotRef)cells[0].Tag!, cells[0]);
            await window.DropCellForCheckAsync(to);

            Assert.Equal("first", source?.Cell?.Macro.Name);
            Assert.Equal("second", target?.Cell?.Macro.Name);
            Assert.False(window.Answer.IsCompleted);
            Assert.True(window.IsVisible);
            window.Close();
            return true;
        });
    }

    [Fact]
    public void Page_controls_are_visible_without_an_edit_mode()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();

            Assert.NotNull(FindButton(window, "AddCommonPageButton"));
            Assert.NotNull(FindButton(window, "AddContextPageButton"));
            window.Close();
        });
    }

    [Fact]
    public void The_first_page_in_each_region_has_no_delete_menu()
    {
        Ui.Run(() =>
        {
            var layout = new MenuRuntimeLayout(
            [
                new MenuRuntimePage("common-1", false, string.Empty, []),
                new MenuRuntimePage("common-2", false, string.Empty, []),
            ],
            [
                new MenuRuntimePage("context-1", false, "Rule", []),
                new MenuRuntimePage("context-2", false, "Rule", []),
            ]);
            var window = new MacroMenuWindow(layout, Actions(), "notepad");
            window.Show();

            Assert.Null(FindButton(window, "CommonPageButton1").ContextMenu);
            Assert.NotNull(FindButton(window, "CommonPageButton2").ContextMenu);
            Assert.Null(FindButton(window, "ContextPageButton1").ContextMenu);
            Assert.NotNull(FindButton(window, "ContextPageButton2").ContextMenu);
            window.Close();
        });
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(0, 1, false)]
    public void A_child_dialog_or_popup_protects_the_menu_from_outside_clicks(
        int childOpen, int popupOpen, bool expected)
        => Assert.Equal(expected, MainWindow.CanCloseMenuFromOutside(childOpen, popupOpen));

    [Fact]
    public void Arrow_navigation_skips_empty_cells()
    {
        Ui.Run(() =>
        {
            var first = new MacroItem { Name = "first" };
            var second = new MacroItem { Name = "second" };
            var page = new MenuRuntimePage("common", false, string.Empty,
            [
                Cell(0, 0, first),
                Cell(0, 3, second),
            ]);
            var layout = new MenuRuntimeLayout([page],
                [new MenuRuntimePage("default", true, "Default", [])]);
            var window = new MacroMenuWindow(layout, Actions(), "notepad");
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
                Cell(0, 0, first),
            ]);
            var commonTwo = new MenuRuntimePage("common-2", false, string.Empty,
            [
                Cell(2, 3, second),
            ]);
            var layout = new MenuRuntimeLayout([commonOne, commonTwo],
                [new MenuRuntimePage("default", true, "Default", [])]);
            var window = new MacroMenuWindow(layout, Actions(), "notepad");
            window.Show();
            window.SetSearchOpen(true);
            Dispatcher.UIThread.RunJobs();

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
    public void Search_includes_the_current_window_rule_region()
    {
        Ui.Run(() =>
        {
            var common = new MenuRuntimePage("common", false, string.Empty, []);
            var contextMacro = new MacroItem { Name = "ContextOnly" };
            var context = new MenuRuntimePage("context", false, "Rule",
            [
                Cell(1, 1, contextMacro),
            ]);
            var window = new MacroMenuWindow(
                new MenuRuntimeLayout([common], [context]), Actions(), "notepad");
            window.Show();
            window.SetSearchOpen(true);
            Dispatcher.UIThread.RunJobs();
            var search = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());

            search.Text = "ContextOnly";
            Dispatcher.UIThread.RunJobs();
            window.HandleKey("Down");

            Assert.Equal("ContextOnly", window.SelectedCell?.Macro.Name);
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
            var window = new MacroMenuWindow(
                new MenuRuntimeLayout([common], [contextOne, contextTwo]),
                Actions(), "notepad");
            window.Show();

            Assert.Equal(3, window.GetVisualDescendants().OfType<Ellipse>().Count());
            Assert.Equal(2, window.ContextPageCount);
            window.Close();
        });
    }

    [Fact]
    public void Square_images_fill_the_cell_and_other_images_do_not()
    {
        Assert.True(MacroMenuWindow.FillsCell(new PixelSize(128, 128)));
        Assert.False(MacroMenuWindow.FillsCell(new PixelSize(128, 72)));
    }

    [Fact]
    public void The_menu_places_its_middle_divider_at_the_pointer_height()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");

            Assert.Equal(new PixelPoint(512, 45),
                window.PlaceNear(new PixelPoint(500, 400),
                    new PixelRect(0, 0, 1000, 1200), 1));
            Assert.Equal(new PixelPoint(618, 133),
                window.PlaceNear(new PixelPoint(980, 700),
                    new PixelRect(0, 0, 1000, 800), 1));
            Assert.Equal(new PixelPoint(-1888, 0),
                window.PlaceNear(new PixelPoint(-1900, 100),
                    new PixelRect(-1920, 0, 1920, 1080), 1));
        });
    }

    [Fact]
    public void Opening_search_keeps_the_divider_under_the_pointer()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            var cursor = new PixelPoint(500, 400);
            var area = new PixelRect(0, 0, 1000, 1200);

            Assert.Equal(667, window.Height);
            Assert.Equal(355, window.DividerCenterOffset);
            Assert.Equal(cursor.Y,
                window.PlaceNear(cursor, area, 1).Y + window.DividerCenterOffset);

            window.SetSearchOpen(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(667, window.Height);
            Assert.Equal(355, window.DividerCenterOffset);
            Assert.Equal(cursor.Y,
                window.PlaceNear(cursor, area, 1).Y + window.DividerCenterOffset);
            window.Close();
        });
    }

    private static MenuRuntimeLayout Layout(string? description = null,
        MenuIconReference? icon = null)
    {
        var macro = new MacroItem { Name = "one", Description = description ?? string.Empty };
        return new MenuRuntimeLayout(
        [
            new MenuRuntimePage("common", false, string.Empty, [Cell(0, 0, macro, icon)]),
        ],
        [new MenuRuntimePage("default", true, "Default", [])]);
    }

    private static MenuRuntimeCell Cell(int row, int column, MacroItem macro,
        MenuIconReference? icon = null)
    {
        var entry = new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = macro.Name,
            MenuIcon = icon,
        };
        return new MenuRuntimeCell(row, column, icon, entry, macro);
    }

    private static Button OccupiedCell(MacroMenuWindow window)
        => window.GetVisualDescendants().OfType<Button>().First(candidate =>
            candidate.Classes.Contains("MenuCell")
            && candidate.Tag is MenuSlotRef { Cell: not null });

    private static Button FindButton(MacroMenuWindow window, string name)
        => window.GetVisualDescendants().OfType<Button>().Single(button =>
            button.Name == name);

    private static Point Waypoint(Window window, Visual control)
        => control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? default;

    private static MenuWindowActions Actions(Action? restore = null,
        Func<MenuSlotRef, MenuSlotRef, Task<MenuRuntimeLayout?>>? move = null,
        Action<MacroItem>? edit = null,
        Action<MacroItem>? debug = null,
        Func<Task<MenuRuntimeLayout?>>? addRule = null,
        Func<Task<bool>>? block = null)
        => new(
            edit ?? (_ => { }),
            debug ?? (_ => { }),
            () => { },
            restore ?? (() => { }),
            addRule ?? (() => Task.FromResult<MenuRuntimeLayout?>(null)),
            block ?? (() => Task.FromResult(false)),
            () => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            move ?? ((_, _) => Task.FromResult<MenuRuntimeLayout?>(null)));
}
