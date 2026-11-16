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
    // The session around the project: the pin, the remembered window size, leaving with unsaved
    // work, and the recovery snapshot written while the project changes. It owns the asking hooks
    // so a check can answer without a dialog.
    /// <summary>Whether the pin on the menu bar is holding the window above the other windows.</summary>
    private bool _alwaysOnTop;

    /// <summary>Set by the tray once the answer to the leaving question is "go".</summary>
    private bool _allowClose;

    /// <summary>Whether the question about unsaved work is on screen, so only one is asked.</summary>
    private bool _askingToSave;

    /// <summary>Writes the unsaved project out as it changes; null until the window is watched.</summary>
    private RecoveryWriter? _recovery;

    /// <summary>
    /// Whether this session put the project into the snapshot. What clears it is what wrote it:
    /// the list comes up empty on every start, so clearing "because it is not dirty" at startup
    /// would throw away the last run's work before the user was even asked about it.
    /// </summary>
    private bool _projectInRecovery;

    /// <summary>
    /// Whether this run has already put the question about work the last one left behind. The
    /// window is announced again every time it comes back from the notification area, which it does
    /// whenever the macro editor closes, and what the snapshot holds by then is the work this run is
    /// already showing rather than the leftovers the question is about.
    /// </summary>
    private bool _recoveryOffered;

    /// <summary>
    /// The icon in the notification area, or null when this machine has none. It is what tells a
    /// close that leaves the program apart from a close that only puts the window out of the way,
    /// which is the difference between asking about unsaved work and saying nothing at all.
    /// </summary>
    internal AppTray? Tray { get; set; }

    /// <summary>
    /// Puts the question about the unsaved project to the user and reports what they chose. It is
    /// a property so that a check can answer without a dialog standing between it and the decision;
    /// nothing else replaces it.
    /// </summary>
    internal Func<Task<ConfirmChoice>> AskToSaveProject { get; set; }

    /// <summary>
    /// Offers back the work a stopped run left behind and reports what the user chose. A property
    /// for the same reason as <see cref="AskToSaveProject"/>: a check can answer without a dialog.
    /// </summary>
    internal Func<RecoveryContents, Task<ConfirmChoice>> AskToRecover { get; set; }

    /// <summary>Keeps the main window above the other windows until the pin is pressed again.</summary>
    private void ToggleAlwaysOnTop(Button button)
    {
        _alwaysOnTop = !_alwaysOnTop;
        Topmost = _alwaysOnTop;

        // Two ways to read the state: the icon changes (`Pin` when free, `PinOff` when pinned) and the
        // button takes the accent while it is on. The hint says what the next press does.
        button.Classes.Set("pinned", _alwaysOnTop);
        if (button.Content is IconView icon)
        {
            icon.Data = _alwaysOnTop ? AppIcons.PinOff : AppIcons.Pin;
        }

        ToolTip.SetTip(button, Strings.Get(_alwaysOnTop ? "Main.AlwaysOnTopCancel" : "Main.AlwaysOnTop"));
    }

    /// <summary>True when a close really stops the program rather than hiding the window.</summary>
    private bool Leaving => Tray is null || Tray.IsLeaving;

    /// <summary>
    /// Applies the size this machine last chose, limited to the desktop that is here now. Position
    /// is deliberately not restored: a monitor may have gone, and a window must not open off-screen.
    /// </summary>
    private void RestoreWindowSize()
    {
        if (LocalSettings.LoadMainWindowSize() is not { } size)
        {
            return;
        }

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        var scale = screen is { Scaling: > 0 } ? screen.Scaling : 1;
        var widest = screen is null ? size.Width : screen.WorkingArea.Width / scale;
        var tallest = screen is null ? size.Height : screen.WorkingArea.Height / scale;

        Width = Math.Clamp(size.Width, MinWidth, Math.Max(MinWidth, widest));
        Height = Math.Clamp(size.Height, MinHeight, Math.Max(MinHeight, tallest));
    }

    /// <summary>Remembers the ordinary size; a maximized or minimized size is not a choice.</summary>
    private void RememberWindowSize()
    {
        if (WindowState == WindowState.Normal)
        {
            LocalSettings.StoreMainWindowSize(Width, Height);
        }
    }

    /// <summary>
    /// Asks about the project before the program stops. A plain close is not the moment for this:
    /// the window only goes out of the way, and everything in it is still there to be saved later.
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        RememberWindowSize();

        if (!_allowClose
            && Leaving
            && DataContext is MainViewModel { HasUnsavedChanges: true })
        {
            // The answer is given before the base call, because the base call is what raises the
            // Closing event and the way out reads the answer there. A handler that ran first and
            // found nothing would take this window for one that had already gone, and would stop
            // the program on top of the very question it is asking.
            e.Cancel = true;

            // And the question itself goes up one turn later: the dialog is a window of its own,
            // and opening one while this one is being closed is not a position to be in.
            Dispatcher.UIThread.Post(() => _ = PromptToSaveProjectAsync());
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Puts the unsaved project to the user, the same question the macro editor asks about one
    /// macro. Only "save" and "discard" let the program stop; dismissing the question means the
    /// user is staying, and the way out the tray started is called off with it.
    /// </summary>
    private async Task PromptToSaveProjectAsync()
    {
        if (_askingToSave || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _askingToSave = true;
        try
        {
            // The window is usually away in the notification area, and a question nobody can see
            // is no question at all: it comes back so the answer can be given.
            if (!IsVisible)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            }

            var choice = await AskToSaveProject();

            if (choice == ConfirmChoice.Primary)
            {
                // A save that was called off — the file picker dismissed — is an answer as well:
                // the project is still unsaved, so the program stays.
                if (!await SaveProjectAsync(viewModel))
                {
                    Tray?.CalledOff();
                    return;
                }
            }
            else if (choice != ConfirmChoice.Secondary)
            {
                Tray?.CalledOff();
                return;
            }

            // The program is going with nothing carried over: the project was just written out,
            // or the user said the unsaved work is not wanted, so the snapshot has no business
            // surviving to be offered back on the next run.
            RecoveryStore.ClearProject();

            _allowClose = true;
            Close();
        }
        finally
        {
            _askingToSave = false;
        }
    }

    /// <summary>
    /// Keeps the recovery snapshot in step with the macro list: written while there is something
    /// to lose, dropped the moment there is not. A run that ends without notice — a crash, or the
    /// machine going down — therefore leaves the work somewhere the next run can find it.
    /// </summary>
    private void WatchUnsavedProject(MainViewModel viewModel)
    {
        _recovery = new RecoveryWriter(() => WriteUnsavedProject(viewModel));

        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.IsDirty)
                or nameof(MainViewModel.HasUnsavedChanges)
                or nameof(MainViewModel.CurrentPath))
            {
                SyncUnsavedProject(viewModel);
            }
        };

        // A change that arrives while the list is already unsaved reaches nothing above: the dirty
        // flag does not move for it, so the snapshot would be left holding the state it had before.
        viewModel.Changed += (_, _) => KeepUnsavedProject(viewModel);

        // The armed state of a macro is written into the package, so a change to it counts, and
        // it moves nothing that would raise a property change on the list itself.
        viewModel.Macros.CollectionChanged += (_, _) => KeepUnsavedProject(viewModel);
        SyncUnsavedProject(viewModel);
    }

    /// <summary>Writes the project out when it first becomes unsaved, and lets the snapshot go
    /// when there is nothing left to lose.</summary>
    private void SyncUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            _recovery?.Stop();

            // Only take back what this session put there. A snapshot left by the last run is not
            // this window's to drop: the list is empty on every start, and clearing it here would
            // do that before the recovered work had even been offered.
            if (_projectInRecovery)
            {
                _projectInRecovery = false;
                RecoveryStore.ClearProject();
            }

            return;
        }

        _projectInRecovery = true;
        _recovery?.Now();
    }

    /// <summary>
    /// Keeps a change that arrived while the list was already unsaved from going unwritten. The
    /// write itself waits for the changes to pause; what makes the work unsaved was written already.
    /// </summary>
    private void KeepUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            return;
        }

        _projectInRecovery = true;
        _recovery?.Changed();
    }

    private static void WriteUnsavedProject(MainViewModel viewModel)
    {
        if (!viewModel.HasUnsavedChanges)
        {
            return;
        }

        RecoveryStore.SaveProject([.. viewModel.Macros], viewModel.Triggers,
            [.. viewModel.Variables.Globals], viewModel.CurrentPath);
    }
}
