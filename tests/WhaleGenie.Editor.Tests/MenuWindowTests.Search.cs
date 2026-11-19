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

// The search box: what takes focus, what collapses it and what escape clears.
public partial class MenuWindowTests
{
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
}
