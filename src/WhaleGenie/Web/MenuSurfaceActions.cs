using System;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>The page's edit/debug/settings commands, carried out by the shell window.</summary>
internal sealed class MenuSurfaceActions(Func<WebShellWindow?> owner) : IMenuSurfaceActions
{
    public void OpenEditor(string macroName) => owner()?.OpenEditor(macroName);

    public void OpenRun(string macroName) => owner()?.OpenRun(macroName);

    public void OpenSettings() => owner()?.OpenSettings();
}
