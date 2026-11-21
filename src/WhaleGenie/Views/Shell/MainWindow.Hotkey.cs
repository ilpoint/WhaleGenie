using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// The surface half of the trigger host: the menu and wheel handlers the host's hooks forward to,
// and the system switches they ask for. The hooks themselves and the hotkey plan live in
// TriggerHost; everything here is this window's own drawing and state.
public partial class MainWindow : AppWindow
{
    /// <summary>Whether a pressed key should answer a macro, rather than the app being edited.</summary>
    private bool WindowTextFieldFocused
        => IsActive && FocusManager?.GetFocusedElement() is TextBox;

    /// <summary>
    /// Flips the system switch from a hotkey. The keyboard-focus check is this window's: a field
    /// on it having the keyboard means the keys were meant for it, not for the macros.
    /// </summary>
    private void ToggleSystemFromHotkey()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (IsActive && FocusManager?.GetFocusedElement() is TextBox)
        {
            return;
        }

        viewModel.IsRunning = !viewModel.IsRunning;
    }

    /// <summary>The same switch, chosen from a macro's own arm/disarm key.</summary>
    private void ToggleMacroFromHotkey(string name)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var macro = viewModel.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            return;
        }

        ToggleMacro(macro, viewModel);
    }

    /// <summary>
    /// Marks the macros the trigger is running, which colours the dots on their cards. The trigger
    /// host updates these too; this local copy answers to a toggle made on the window itself.
    /// </summary>
    private void RefreshRunning(MainViewModel? viewModel)
    {
        if (_triggers is null || viewModel is null)
        {
            return;
        }

        foreach (var macro in viewModel.Macros)
        {
            macro.IsRunning = _triggers.IsRunning(macro.Name);
        }
    }

    private void StopTriggers()
    {
        if (_triggers is null)
        {
            return;
        }

        // The container owns the service, so this only stops it: the window that shows what a
        // trigger did is going away, and nothing should keep triggering behind it.
        _triggers.IsEnabled = false;
        _triggers.StopAll();
        _triggers = null;
    }
}
