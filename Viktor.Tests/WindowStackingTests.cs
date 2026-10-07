using Avalonia.Controls;
using Viktor.Views;

namespace Viktor.Tests;

/// <summary>
/// A window pinned on top has to drag along the windows opened from it. Windows keeps a topmost
/// window above every window that is not topmost — owner or not — so the add-action dialog opened
/// from a pinned editor would otherwise come up underneath the editor that asked for it.
/// </summary>
public class WindowStackingTests
{
    [Fact]
    public void A_dialog_of_a_pinned_window_is_pinned_too()
    {
        Ui.Run(() =>
        {
            var owner = OnScreen(new Window { Topmost = true });
            var dialog = new Window();

            DialogWindow.ShowDialogOver<object?>(dialog, owner);

            Assert.True(dialog.Topmost);
            dialog.Close();
        });
    }

    [Fact]
    public void A_dialog_of_an_ordinary_window_is_left_alone()
    {
        Ui.Run(() =>
        {
            var owner = OnScreen(new Window());
            var dialog = new Window();

            DialogWindow.ShowDialogOver<object?>(dialog, owner);

            Assert.False(dialog.Topmost);
            dialog.Close();
        });
    }

    [Fact]
    public void A_window_that_pins_itself_keeps_its_pin()
    {
        // The screen pickers pin themselves, and opening one from an ordinary window must not
        // take that away.
        Ui.Run(() =>
        {
            var owner = OnScreen(new Window());
            var picker = new Window { Topmost = true };

            DialogWindow.ShowOver(picker, owner);

            Assert.True(picker.Topmost);
            picker.Close();
        });
    }

    /// <summary>Shows a window, because only a window that is on screen can own another.</summary>
    private static Window OnScreen(Window window)
    {
        window.Show();
        return window;
    }
}
