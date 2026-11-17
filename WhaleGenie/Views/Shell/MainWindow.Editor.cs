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

// Opening the macro editor, and what the window does with what the editor hands back.
public partial class MainWindow : AppWindow
{
    /// <summary>
    /// Opens the macro editor on a macro: one from the list, a new one (<paramref name="editing"/> is
    /// null), or a draft recovered from a run that stopped without notice. What that macro replaces
    /// when it is saved is <paramref name="existing"/>, which is null for a new one. It answers the
    /// editor back so that a check can write in it; nothing else reads that.
    /// </summary>
    internal MacroEditorWindow? OpenEditor(MacroItem? editing, MacroItem? existing, bool draft,
        MacroView? newView = null, IReadOnlyList<TriggerEntry>? entries = null)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return null;
        }

        // Two editors on one macro would each hand back their own version and the last one closed
        // would win, so the editor already open is the one wanted; it is brought to the front.
        if (_editor is { } open)
        {
            open.Activate();
            return open;
        }

        // The package path is what a picture taken from the screen is stored beside, and the device
        // layer is the program's own, so the trials and the runs started from here use the one layer
        // whose text recognition models are already loaded.
        var editor = new MacroEditorWindow(editing, [.. viewModel.Macros], viewModel.CurrentPath,
            SharedDevices.Current, viewModel.Triggers, callableOnly: false, newView,
            viewModel.Persisted, entries: entries);

        // Recovered work is unsaved by definition, so closing that editor has to ask about it rather
        // than let it go without a word.
        if (draft)
        {
            editor.MarkUnsaved();
        }

        var commit = new EditorCommitState(existing, newView ?? MacroView.All);
        _editor = editor;
        editor.Commit = draft => CommitEditorSnapshot(editor, draft, commit, viewModel);
        editor.Closed += (_, _) => OnEditorClosed(editor, commit, viewModel);

        // The editor stands beside the list rather than replacing it, so both stay on screen and
        // either can be minimized without taking the other down.
        editor.ShowAsPeer(this);
        return editor;
    }

    /// <summary>
    /// Takes what the editor ended with: the macro joins the list, or the one that was edited
    /// replaces it. Then the list comes back, unless the user is on their way out of the program.
    /// </summary>
    private void OnEditorClosed(MacroEditorWindow editor, EditorCommitState commit,
        MainViewModel viewModel)
    {
        _editor = null;

        if (editor.SteppedAside && !(Tray?.IsLeaving ?? false))
        {
            Tray?.ComeBack();
        }

        if (editor.Result is not { } draft)
        {
            return;
        }

        CommitEditorSnapshot(editor, draft, commit, viewModel);
    }

    /// <summary>
    /// Takes what the editor ended with and answers whether the project took it. A macro that has
    /// left the list cannot be written back: a draft applied from MCP replaces the list's instances,
    /// and replacing one that is no longer there would silently drop the edit. False leaves the
    /// work in the editor, which shows why and keeps its draft.
    /// </summary>
    private bool CommitEditorSnapshot(MacroEditorWindow editor, MacroDraft draft,
        EditorCommitState commit, MainViewModel viewModel)
    {
        var macro = draft.Macro;
        if (commit.Target is { } target && !viewModel.Macros.Contains(target))
        {
            return false;
        }

        // A stretch of steps lifted into a macro of its own during the session is real work: it
        // joins the project together with the macro that calls it, because the call is only in the
        // list that is arriving now.
        foreach (var lifted in editor.LiftedMacros)
        {
            if (!commit.Lifted.Add(lifted))
            {
                continue;
            }

            viewModel.AddMacro(lifted);
        }

        if (commit.Target is null)
        {
            ApplyEditorTriggerSettings(viewModel, draft.Layouts);
            viewModel.AddMacro(macro, draft.Entries);
            PlaceNewMacro(commit.NewView, macro, viewModel);
            commit.Target = macro;
            _recovery?.Follow();
            return true;
        }

        // Saving an unchanged macro is not a project change. This also makes the close after a
        // successful save a no-op rather than a second insertion or replacement.
        if (Same(viewModel, commit.Target, draft))
        {
            return true;
        }

        // The edited macro replaces the one the trigger may be running.
        _triggers?.Stop(commit.Target.Name);
        ApplyEditorTriggerSettings(viewModel, draft.Layouts);
        if (!viewModel.ReplaceMacro(commit.Target, macro, draft.Entries))
        {
            return false;
        }

        commit.Target = macro;
        RefreshRunning(viewModel);

        // The editor has just let its draft go, so the list is the only copy of this macro: it
        // goes into the snapshot now rather than waiting for the next turn of the timer.
        _recovery?.Follow();
        return true;
    }

    /// <summary>
    /// Puts a macro that was created from the menu or the wheel view where that view keeps things.
    /// It happens after the save because a position names the macro, and a macro is named by the
    /// editor. The key, window and string views already brought their own entry with them.
    /// </summary>
    internal static void PlaceNewMacro(MacroView view, MacroItem macro, MainViewModel viewModel)
    {
        switch (view)
        {
            case MacroView.Menu when viewModel.Triggers.FirstFreeMenuCell() is { } cell:
                viewModel.Triggers.Entries.Add(new TriggerEntry
                {
                    Spec = new MenuSpec(cell.PageId, cell.Row, cell.Column),
                    MacroName = macro.Name,
                    IsEnabled = true,
                });
                break;
            case MacroView.Wheel when viewModel.Triggers.Wheel.Global.FirstEmptySlot() is { } slot:
                slot.Macro = macro.Name;
                viewModel.Triggers.NotifyChanged();
                break;
        }
    }

    /// <summary>
    /// Whether the editor brought back the very same macro, triggers and layouts. The editor builds
    /// the macro again as it hands it over, so this is a question about what the macro says rather
    /// than about which object it is; two macros that read the same are written the same way.
    /// </summary>
    private static bool Same(MainViewModel viewModel, MacroItem target, MacroDraft draft)
        => string.Equals(target.ToJson().ToJsonString(), draft.Macro.ToJson().ToJsonString(),
            StringComparison.Ordinal)
           && string.Equals(TriggerJson(viewModel.Triggers.ForMacro(target.Name)),
               TriggerJson(draft.Entries), StringComparison.Ordinal)
           && string.Equals(LayoutJson(viewModel.Triggers.InterfaceLayouts),
               LayoutJson(draft.Layouts), StringComparison.Ordinal);

    private static string TriggerJson(IReadOnlyList<TriggerEntry> entries)
    {
        var array = new JsonArray();
        foreach (var entry in entries)
        {
            array.Add(entry.ToJson());
        }

        return array.ToJsonString();
    }

    private static string LayoutJson(IReadOnlyList<InterfaceLayout> layouts)
    {
        var array = new JsonArray();
        foreach (var layout in layouts)
        {
            array.Add(layout.ToJson());
        }

        return array.ToJsonString();
    }

    private static void ApplyEditorTriggerSettings(MainViewModel viewModel,
        IReadOnlyList<InterfaceLayout> layouts)
    {
        foreach (var layout in layouts)
        {
            viewModel.Triggers.StoreInterfaceHotkey(layout.Window, layout.ToggleHotkey);
        }
    }
}
