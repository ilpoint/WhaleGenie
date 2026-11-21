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

// Arrow navigation, and what a search scans and keeps in place.
public partial class MenuWindowTests
{

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
        => Assert.Equal(expected, NativeSurfaceHost.CanCloseMenuFromOutside(childOpen, popupOpen));

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
}
