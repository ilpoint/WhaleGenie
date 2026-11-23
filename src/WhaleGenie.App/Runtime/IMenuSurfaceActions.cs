namespace WhaleGenie.Runtime;

/// <summary>
/// What the menu page can ask the interface to do that is not a project edit: open a macro, start a
/// debug run, or pick an icon with the native picker.
/// </summary>
public interface IMenuSurfaceActions
{
    void OpenEditor(string macroName);

    void OpenRun(string macroName);

    /// <summary>Closes the menu and opens the settings window, the menu's gear button.</summary>
    void OpenSettings();
}
