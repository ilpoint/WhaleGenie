using System.Threading.Tasks;
using Avalonia.Controls;

namespace WhaleGenie.Views;

/// <summary>
/// Opens a window over the window that asked for it, which is what keeps a pinned window's
/// dialogs in front of it.
/// </summary>
/// <remarks>
/// Windows keeps a topmost window above every window that is not topmost — owner or not — so a
/// dialog opened from a pinned window comes up underneath the very window that asked for it.
/// Avalonia's <see cref="WindowBase.Topmost"/> is the window's own setting and says nothing about
/// the windows opened from it, so the pin has to travel down the chain by hand: the main window
/// is pinned, so the editor opened from it is pinned, so the add-action dialog opened from the
/// editor is pinned, and each one sits above the one that opened it as ownership normally works.
/// </remarks>
internal static class DialogWindow
{
    /// <summary>Shows a dialog owned by <paramref name="owner"/> and waits for its result.</summary>
    public static Task<TResult> ShowDialogOver<TResult>(this Window dialog, Window owner)
    {
        Pin(dialog, owner);
        return dialog.ShowDialog<TResult>(owner);
    }

    /// <summary>The same, for a dialog that does not hand a result back.</summary>
    public static Task ShowDialogOver(this Window dialog, Window owner)
    {
        Pin(dialog, owner);
        return dialog.ShowDialog(owner);
    }

    /// <summary>The same, for a window shown without waiting for it.</summary>
    public static void ShowOver(this Window window, Window owner)
    {
        Pin(window, owner);
        window.Show(owner);
    }

    /// <summary>
    /// Shows a window beside <paramref name="owner"/> rather than under it: the pin is still
    /// matched, but the window is not owned, so the owner going away does not take it with it. The
    /// macro editor is the one that needs this — the main window steps aside into the notification
    /// area while a macro is being written, and an owned window would go with it.
    /// </summary>
    public static void ShowAsPeer(this Window window, Window owner)
    {
        Pin(window, owner);
        window.Show();
    }

    /// <summary>
    /// Matches a window's pin to the owner's. Only ever turns the pin on: a picker pins itself on
    /// purpose, and opening one from an ordinary window must not undo that.
    /// </summary>
    private static void Pin(Window window, Window owner)
    {
        if (owner.Topmost)
        {
            window.Topmost = true;
        }
    }
}
