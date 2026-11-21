using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace WhaleGenie.Runtime;

/// <summary>
/// Asks the window that really has the keyboard whether a text field is taking it. The web shell
/// and the native main window each copy their focus state into ITriggerFocusProbe; the trigger host
/// must ask the active one, not the hidden window it happened to be built beside.
/// </summary>
/// <remarks>
/// The answer is read on the interface thread, where the host checks it before turning a hotkey
/// into a system or macro switch. A window that is not the active one is not typing, whatever its
/// last copied state says.
/// </remarks>
public sealed class WindowFocusProbe : ITriggerFocusProbe
{
    private readonly Func<Window?>? _active;

    public WindowFocusProbe()
    {
    }

    /// <summary>A check may hand in the window it wants asked; the program asks the active one.</summary>
    internal WindowFocusProbe(Func<Window?> active) => _active = active;

    public bool IsTextInputFocused
    {
        get
        {
            if (_active is not null)
            {
                return _active() is ITriggerFocusProbe probe && probe.IsTextInputFocused;
            }

            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            {
                return false;
            }

            return desktop.Windows.FirstOrDefault(window => window.IsActive) is ITriggerFocusProbe active
                && active.IsTextInputFocused;
        }
    }
}
