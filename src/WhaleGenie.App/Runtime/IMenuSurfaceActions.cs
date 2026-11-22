using System.Threading.Tasks;

namespace WhaleGenie.Runtime;

/// <summary>
/// What the menu page can ask the interface to do that is not a project edit: open a macro, start a
/// debug run, or pick an icon with the native picker.
/// </summary>
public interface IMenuSurfaceActions
{
    void OpenEditor(string macroName);

    void OpenRun(string macroName);

    /// <summary>The icon the person picked, as the wire spells it, or null when called off.</summary>
    Task<(string Kind, string Value)?> PickIconAsync();
}
