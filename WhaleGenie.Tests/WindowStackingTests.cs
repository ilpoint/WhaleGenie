using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

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
        Ui.RunAsync(async () =>
        {
            var owner = OnScreen(new Window { Topmost = true });
            var dialog = new Window();

            // ShowDialog starts a modal dispatcher loop. It has to start and finish inside this
            // check: a close posted before the loop exists, or a task that is never awaited,
            // leaves the loop for whichever unrelated check runs next.
            var loop = DialogWindow.ShowDialogOver<object?>(dialog, owner);
            Dispatcher.UIThread.RunJobs();

            Assert.True(dialog.Topmost);
            dialog.Close();
            await loop;
            owner.Close();
            return true;
        });
    }

    [Fact]
    public void A_dialog_of_an_ordinary_window_is_left_alone()
    {
        Ui.RunAsync(async () =>
        {
            var owner = OnScreen(new Window());
            var dialog = new Window();

            var loop = DialogWindow.ShowDialogOver<object?>(dialog, owner);
            Dispatcher.UIThread.RunJobs();

            Assert.False(dialog.Topmost);
            dialog.Close();
            await loop;
            owner.Close();
            return true;
        });
    }

    [Fact]
    public void A_window_that_pins_itself_keeps_its_pin()
    {
        // A window that pins itself on purpose keeps its pin: opening it from an ordinary window
        // must not take that away.
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
