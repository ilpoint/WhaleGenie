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

    /// <summary>Raised whenever the overlay goes away, whichever path closed it.</summary>
    event Action? Hidden;

    /// <summary>Raised when the page's search field takes or loses the keyboard.</summary>
    event Action<bool>? FocusChanged;

    /// <summary>Told by the page when its search field takes or loses the keyboard.</summary>
    void SetSearchFocused(bool focused);

    void Show();

    void Hide();
}