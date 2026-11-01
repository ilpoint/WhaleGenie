using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;

namespace WhaleGenie.Tests;

/// <summary>
/// A right-click menu is a popup with no template of its own: it is drawn by whatever theme supplies
/// one. Moving to FluentAvalonia removed <c>FluentTheme</c> from the application and the library does
/// not carry one for <see cref="ContextMenu"/>, so every menu in the program opened, reported itself
/// open, and painted nothing at all. Nothing in a headless check noticed, because a headless check
/// does not look at pixels — what it can check is that the theme is there and that the opened menu
/// has a size and a presenter.
/// </summary>
public class MenuThemeTests
{
    [Fact]
    public void A_context_menu_has_a_theme_to_draw_itself_with()
    {
        var found = Ui.Run(() =>
            Application.Current!.TryFindResource(typeof(ContextMenu), out var theme) && theme is not null);

        Assert.True(found, "no ContextMenu control theme: every right-click menu would draw nothing");
    }

    [Fact]
    public void A_menu_separator_has_a_theme_to_draw_itself_with()
    {
        var found = Ui.Run(() =>
            Application.Current!.TryFindResource(typeof(Separator), out var theme) && theme is not null);

        Assert.True(found, "no Separator control theme: menu dividers would be invisible");
    }

    [Fact]
    public void An_opened_menu_has_a_size_and_a_presenter()
    {
        var (width, presented, items) = Ui.Run(() =>
        {
            var window = new Window { Width = 320, Height = 240 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var host = new Border { Background = Avalonia.Media.Brushes.Transparent };
            var menu = new ContextMenu();
            menu.Items.Add(new MenuItem { Header = Strings.Get("Run.BreakpointSet") });
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = Strings.Get("Run.BreakpointCondition") });
            host.ContextMenu = menu;
            window.Content = host;

            Dispatcher.UIThread.RunJobs();
            menu.Open(host);
            Dispatcher.UIThread.RunJobs();

            var width = menu.Bounds.Width;
            var presented = menu.GetVisualDescendants().OfType<ItemsPresenter>().Any();
            var items = menu.GetVisualDescendants().OfType<MenuItem>().Count();

            menu.Close();
            window.Close();
            return (width, presented, items);
        });

        Assert.True(width > 0, "the opened menu measured to nothing, which is a menu nobody can see");
        Assert.True(presented, "the menu has no items presenter, so its items are not drawn");
        Assert.Equal(2, items);
    }
}
