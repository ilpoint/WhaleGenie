using System;
using System.Collections.Generic;
using Avalonia.Controls;
using WhaleGenie.Bridge;
using WhaleGenie.Runtime;

namespace WhaleGenie.Web;

/// <summary>
/// The two shell windows, and the one answer to "which window is the person in". The pickers,
/// native dialogs and the trigger focus probe ask this instead of remembering the main window:
/// the editor workbench is a real second shell, not an overlay.
/// </summary>
public sealed class ShellWindowRegistry : ITriggerFocusProbe
{
    private readonly List<WebShellWindow> _windows = [];

    internal WebShellWindow? Main { get; private set; }

    internal WebShellWindow? Workbench { get; private set; }

    /// <summary>The shell that was last active; the main window while neither has the focus.</summary>
    public Window? ActiveShell
        => (_windows.FindLast(window => window.IsActive) ?? Main ?? Workbench) as Window;

    public bool IsTextInputFocused
        => _windows.Exists(window => window.IsActive && window.TextInputFocused);

    internal void Register(WebShellWindow window)
    {
        if (!_windows.Contains(window))
        {
            _windows.Add(window);
        }

        if (window.Surface == BridgeSurface.Workbench)
        {
            Workbench = window;
        }
        else
        {
            Main = window;
        }
    }

    internal void Unregister(WebShellWindow window)
    {
        _windows.Remove(window);
        if (ReferenceEquals(Workbench, window))
        {
            Workbench = null;
        }

        if (ReferenceEquals(Main, window))
        {
            Main = null;
        }
    }
}
