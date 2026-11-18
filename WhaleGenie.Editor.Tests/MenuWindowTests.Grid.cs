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

// The menu as a grid: the macro picker, the four by six cells and the title bar.
public partial class MenuWindowTests
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
            Assert.Equal(6, ProjectTriggers.MenuRegionRows * 2);
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
}
