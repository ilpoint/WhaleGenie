namespace WhaleGenie.Runtime;

/// <summary>
/// The separate macro workbench window: the editor and run/debug pages live here, away from the
/// main shell's macro list. The main window asks this host to open a document or a run; the host
/// owns the one workbench window and its close question, not a second trigger/surface host.
/// </summary>
public interface IWorkbenchHost
{
    /// <summary>Whether the workbench window is on screen.</summary>
    bool IsOpen { get; }

    /// <summary>Shows or activates the workbench without changing its page.</summary>
    void Show();

    /// <summary>Shows the workbench and asks it to open the named macro; an empty name means new.</summary>
    void OpenEditor(string? name);

    /// <summary>Shows the workbench and loads the named macro into the run/debug page.</summary>
    void OpenRun(string name);

    /// <summary>Brings the main shell back, used by the workbench's "back to main" door.</summary>
    void FocusMain();
}
