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

public partial class MainWindow : AppWindow
{
    /// <summary>Runs the macros the list is waiting for; null until the window is open.</summary>
    private MacroTriggerService? _triggers;

    /// <summary>
    /// A plain copy of the system switch for the hook thread. Avalonia's DataContext and the
    /// view model may only be read on the interface thread: reading them from a hook callback
    /// throws InvalidOperationException, which used to leave the wheel gesture permanently idle.
    /// </summary>
    private int _systemRunning;

    /// <summary>
    /// The macro editor on screen, or null while there is none. One at a time, because two of them
    /// on one macro would hand back two versions of it and the last one closed would win.
    /// </summary>
    private MacroEditorWindow? _editor;

    /// <summary>
    /// The settings window on screen, or null while there is none. It stands beside the main window
    /// rather than over it: the wheel page is edited while the thing it edits is on the desk, and a
    /// modal window would be put away by every picker that has to look at the desk.
    /// </summary>
    private SettingsWindow? _settings;

    /// <summary>
    /// The macro an open editor currently replaces in the project. Save commits a fresh snapshot,
    /// so this moves forward with every successful save and keeps close from applying it twice.
    /// </summary>
    private sealed class EditorCommitState(MacroItem? target, MacroView newView)
    {
        public MacroItem? Target { get; set; } = target;

        /// <summary>
        /// The view the macro was created from. A macro made from the menu or the wheel view is
        /// placed there once it has a name, which is the only moment a position can point at it.
        /// </summary>
        public MacroView NewView { get; } = newView;

        public HashSet<MacroItem> Lifted { get; } = [];
    }

    /// <summary>
    /// The run-and-debug window on screen, or null while there is none. One at a time for the same
    /// reason: it debugs one macro, and asking for another puts those steps in the window that is
    /// already open rather than laying a second one over the same list.
    /// </summary>
    private RunWindow? _runDebug;

    /// <summary>
    /// The run-and-debug window on screen, or null while there is none. A property so that a check
    /// can see which window the button opened without going through the desktop's own list.
    /// </summary>
    internal RunWindow? RunDebug => _runDebug;

    /// <summary>The shared hook the Home key is answered through; null until the window is open.</summary>
    private IInputHook? _hotkeyHook;

    private readonly HashSet<string> _globalHeld = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _hiddenInterfaceGroups = new(StringComparer.Ordinal);
    private TriggerRuntime? _runtime;
    private GlobalHotkeyPlan _globalHotkeyPlan = GlobalHotkeyPlan.Empty;

    private static NativePoint? GetCursorPosition()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return GetCursorPos(out var point) ? point : null;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
