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

// Opening the run window, and offering work recovered from a run that stopped without notice.
public partial class MainWindow : AppWindow
{
    /// <summary>
    /// Opens the run-and-debug window, or brings the one already open to the front. Asked for
    /// without a macro it shows the variables and nothing to run, and offers to pick one and start
    /// it; the card's own button and menu hand in the macro they are on instead.
    /// </summary>
    private async Task ShowRunDebugAsync(MacroItem? macro = null)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (_runDebug is { } open)
        {
            // The window is put away rather than gone: it may have been minimized to the taskbar
            // or hidden by the scope that took the screen away for a moment. Bring it back before
            // anything else, or the run it is showing goes on with nobody watching.
            open.BringBack();
            if (macro is not null)
            {
                await open.DebugAsync(macro);
            }

            return;
        }

        // The shared variables are written into the package with the macros, so creating or editing
        // one is a change to the project like any other — and nothing on the macro list moves when
        // it happens, so this is the one place that can notice it. The window is not modal, so when
        // it is closed is when to look.
        var before = viewModel.Globals();

        // The layer the whole program shares, so a debug run reads with the same loaded models the
        // rest of the program uses rather than loading a second copy of them.
        var window = new RunWindow([], SharedDevices.Current, listed: [.. viewModel.Macros],
            triggers: _triggers, variables: viewModel.Variables);
        _runDebug = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_runDebug, window))
            {
                _runDebug = null;
            }

            if (viewModel.GlobalsChangedSince(before))
            {
                viewModel.MarkGlobalsChanged();
            }
        };

        // The run window stands beside the list rather than under it: the list goes into the
        // notification area whenever it is minimized, and an owned window would be taken down
        // with it — a run being watched would lose the window watching it, and the button that
        // opens it would find a window that is not on the screen.
        window.ShowAsPeer(this);

        if (macro is not null)
        {
            await window.DebugAsync(macro);
        }
    }

    // -------------------------------------------------------------- macro package

    // -------------------------------------------------------------- recovery

    /// <summary>
    /// Offers back whatever the last run left unsaved. The list comes up empty, so this is the one
    /// moment the snapshot can be read: the work it holds is not in any package yet. It is asked
    /// once for the whole run — when the real application starts, and directly by a check.
    /// </summary>
    internal async Task OfferRecoveryAsync()
    {
        if (_recoveryOffered || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        _recoveryOffered = true;

        if (RecoveryStore.Load() is not { } contents)
        {
            return;
        }

        switch (await AskToRecover(contents))
        {
            case ConfirmChoice.Primary:
                Recover(viewModel, contents);
                break;
            case ConfirmChoice.Secondary:
                // The work is not wanted, so it goes for good rather than being asked about
                // again on the next run.
                RecoveryStore.Clear();
                break;
            default:
                // Put off, not answered: the snapshot stays, so the next run asks again.
                break;
        }
    }

    private void Recover(MainViewModel viewModel, RecoveryContents contents)
    {
        viewModel.RestoreFrom(contents);
        RefreshRunning(viewModel);

        // An editor that was open is opened again on the macro it was holding, so that work comes
        // back too and not only the list behind it.
        if (contents.Editor is { } editor)
        {
            var target = editor.Replaces is { Length: > 0 } name
                ? viewModel.Macros.FirstOrDefault(macro =>
                    string.Equals(macro.Name, name, StringComparison.OrdinalIgnoreCase))
                : null;

            OpenEditor(editor.Macro, target, draft: true, entries: editor.Entries);
        }
    }

    /// <summary>Says what the snapshot holds, one place at a time, for the question.</summary>
    private static string RecoveryMessage(RecoveryContents contents)
    {
        var found = new List<string>();
        if (contents.Macros.Count > 0 || contents.PackagePath is not null)
        {
            found.Add(Strings.Format("Recover.Project", contents.Macros.Count));
        }

        if (contents.Editor is not null)
        {
            found.Add(Strings.Get("Recover.Editor"));
        }

        return Strings.Format("Recover.Message", string.Join(Environment.NewLine, found));
    }
}
