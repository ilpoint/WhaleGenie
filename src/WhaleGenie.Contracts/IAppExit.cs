namespace WhaleGenie.Contracts;

/// <summary>
/// The exit the page asks for. The tray owns the unsaved-work questions and the window lifecycle,
/// so the handler only says "leave"; the UI project decides what leaving means.
/// </summary>
public interface IAppExit
{
    void RequestExit();
}
