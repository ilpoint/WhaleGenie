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

// A pinned menu, and the footer when there is no window to act on.
public partial class MenuWindowTests
{
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
}
