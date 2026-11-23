using System;

namespace WhaleGenie.Runtime;

/// <summary>
/// The menu as an overlay window: the bridge can close it after a macro runs, and the shell can
/// show it again without rebuilding the page.
/// </summary>
public interface IMenuOverlay
{
    bool IsVisible { get; }

    /// <summary>Pinned menus stay open when another window takes the focus.</summary>
    bool Pinned { get; set; }

    /// <summary>
    /// True while a picker owned by the menu is open: the picker becomes the foreground window,
    /// and that must not be mistaken for the person moving on to another program.
    /// </summary>
    bool AutoCloseSuspended { get; set; }

    /// <summary>Starts a system move of the window, so it can follow the pointer off its own bounds.</summary>
    void BeginDrag();

    /// <summary>
    /// A mouse press anywhere on screen: a press outside the menu closes it unless the menu is
    /// pinned or one of its pickers is open. The hook is what lets a press on the shell, the
    /// desktop or a game count even when the foreground window does not change.
    /// </summary>
    void MouseDown(OverlayPoint point);

    /// <summary>Raised whenever the overlay goes away, whichever path closed it.</summary>
    event Action? Hidden;

    /// <summary>Raised when the page's search field takes or loses the keyboard.</summary>
    event Action<bool>? FocusChanged;

    /// <summary>Told by the page when its search field takes or loses the keyboard.</summary>
    void SetSearchFocused(bool focused);

    void Show();

    /// <summary>Builds the window and loads its page ahead of the first open.</summary>
    void Warm();

    void Hide();
}