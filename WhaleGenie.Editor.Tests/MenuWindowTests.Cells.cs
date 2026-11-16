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

// A cell: its four actions, its menu, its icon, and the click that runs it.
public partial class MenuWindowTests
{
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
            Assert.True(window.ShowActivated,
                "the popup needs a window that is allowed to activate while it is open");
            cell.ContextMenu.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.HasOpenPopup);
            Assert.False(window.ShowActivated,
                "the menu must return to not taking focus once the popup closes");
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
}
