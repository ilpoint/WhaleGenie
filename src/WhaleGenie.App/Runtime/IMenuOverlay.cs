namespace WhaleGenie.Runtime;

/// <summary>
/// The menu as an overlay window: the bridge can close it after a macro runs, and the shell can
/// show it again without rebuilding the page.
/// </summary>
public interface IMenuOverlay
{
    bool IsVisible { get; }

    void Show();

    void Hide();
}
