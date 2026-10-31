using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The five docks above the list are tabs in all but name: one of them is the dock being shown, and
/// it says so with an underline in the accent colour. Nothing used to hold that still — the
/// underline is a border drawn by a class, and a class that stops being applied looks exactly like
/// a window with no dock chosen.
/// </summary>
public class MainDockTabTests
{
    [Fact]
    public void The_chosen_dock_is_the_tab_that_paints_the_underline()
    {
        Ui.Run(() =>
        {
            var window = new MainWindow { DataContext = new MainViewModel() };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                var tabs = window.GetVisualDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("Tab") && button.Classes.Contains("Root"))
                    .ToList();
                Assert.Equal(5, tabs.Count);

                // One dock is always chosen, so exactly one tab carries the class — five unlit tabs
                // is the bug this case exists to catch.
                var keys = Assert.Single(tabs, tab => tab.Classes.Contains("selected"));
                Assert.Same(tabs[0], keys);

                // The underline is a two pixel bottom border, and it is the accent colour rather
                // than a grey: the difference between "this is the page" and "this is a divider".
                Assert.Equal(new Thickness(0, 0, 0, 2), keys.BorderThickness);
                Assert.Equal(Token("AccentFill"), Colour(keys.BorderBrush));

                // Choosing another dock moves both the class and the underline.
                var viewModel = (MainViewModel)window.DataContext!;
                viewModel.SelectedDock = DockPage.Menu;
                Dispatcher.UIThread.RunJobs();

                var menu = Assert.Single(tabs, tab => tab.Classes.Contains("selected"));
                Assert.Same(tabs[3], menu);
                Assert.Equal(Token("AccentFill"), Colour(menu.BorderBrush));
                Assert.DoesNotContain("selected", tabs[0].Classes);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Color Token(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Dark, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }

    private static Color? Colour(IBrush? brush) => (brush as ISolidColorBrush)?.Color;
}
