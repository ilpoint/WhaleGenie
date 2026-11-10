using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

public partial class MacroEditorWindow : AppWindow
{
    // Running and looking things up from the editor: revealing a step, the calls a step can offer,
    // the library a nested editor gets, and the run, delay and clear commands.
    /// <summary>
    /// Points the list at a step somebody asked to be shown: it is opened out of whatever block it
    /// sits in, scrolled to and selected. This is the answer to "which steps read this variable" —
    /// the dialog that asked keeps standing beside the list, so looking at the step is done beside
    /// filling the field rather than instead of it.
    /// </summary>
    private void LookAtStep(MacroStep step)
    {
        if (!_viewModel.Reveal(step))
        {
            return;
        }

        _viewModel.SetSelection([step]);
        _viewModel.RefreshSelection();

        if (_viewModel.Rows.FirstOrDefault(row => row.IsStep && ReferenceEquals(row.Step, step))
            is { } row)
        {
            // The tree does not follow the selection (see the comment on `StepList`), so the row
            // a step was revealed on is brought into view once the containers exist.
            Dispatcher.UIThread.Post(() =>
            {
                var container = _stepList?.GetVisualDescendants()
                    .OfType<TreeViewItem>()
                    .FirstOrDefault(item => ReferenceEquals(item.DataContext, row));
                container?.BringIntoView();
            });
        }
    }

    /// <summary>
    /// The macros a "run another macro" step can be pointed at, with what each of them declares:
    /// the dialog builds the call's form out of that, so a step calls a macro by the values that
    /// macro asks for rather than by whatever the author remembers to write.
    /// </summary>
    private IReadOnlyList<MacroCallable> MacroCalls()
    {
        var calls = new List<MacroCallable>();
        foreach (var macro in _session.Project.Where(macro => !ReferenceEquals(macro, _session.Editing)))
        {
            if (macro.Name.Length > 0
                && !calls.Any(call => string.Equals(call.Name, macro.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                calls.Add(macro.Callable);
            }
        }

        // Parent and child macros are offered as they stand, so a call can be pointed at unsaved
        // work without leaving the editor.
        foreach (var page in _documentPages)
        {
            var callable = page.Editor.BuildMacro().Callable;
            if (callable.Name.Length > 0
                && !calls.Any(call => string.Equals(call.Name, callable.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                calls.Add(callable);
            }
        }

        // And the submacros of this document by the names a run looks them up by, whether or not a
        // page is open for them: that is the list the 宏 field beside a call step shows.
        foreach (var callable in CallableMacros.Callables(DocumentSubMacros()))
        {
            if (callable.Name.Length > 0
                && !calls.Any(call => string.Equals(call.Name, callable.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                calls.Add(callable);
            }
        }

        return calls;
    }

    /// <summary>
    /// The macros a run started here may call, with the macro being edited taken as it stands
    /// rather than as it was last saved.
    /// </summary>
    internal ProjectMacroLibrary BuildLibrary()
    {
        var entries = new List<(string Name, IReadOnlyList<ExecutableStep> Steps,
            MacroSignature Signature)>();

        foreach (var macro in _session.Project.Where(macro => !ReferenceEquals(macro, _session.Editing)))
        {
            if (macro.Name.Length > 0)
            {
                entries.Add((macro.Name, macro.Steps.ToExecutable(), macro.Callable.Signature));
            }
        }

        foreach (var page in _documentPages)
        {
            var edited = page.Editor.BuildMacro();
            if (edited.Name.Length > 0
                && !entries.Any(entry => string.Equals(entry.Name, edited.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add((edited.Name, page.Editor.Steps.ToExecutable(),
                    edited.Callable.Signature));
            }
        }

        // The submacros of the document as it stands, under the names a call writes: pressing 运行
        // here has to reach the same macros the saved file would.
        foreach (var entry in CallableMacros.Entries(DocumentSubMacros()))
        {
            if (!entries.Any(known => string.Equals(known.Name, entry.Name,
                    StringComparison.OrdinalIgnoreCase)))
            {
                entries.Add(entry);
            }
        }

        return new ProjectMacroLibrary(entries);
    }

    /// <summary>
    /// Opens the run window on the steps as they stand, without saving first. It loads them and stops
    /// there, the way the card's own menu does: a run that starts on its own has already done
    /// something by the time anyone has read the first step, and the reason to open this window is
    /// usually to look at the steps, put a breakpoint in and then press 「运行」. What to do with the
    /// macro is the window's business from then on.
    ///
    /// One of these windows is open at a time, so pressing the button twice does not leave two of
    /// them behind: the one already open is closed first — which stops whatever it was running — and
    /// the steps as they now stand go into a fresh window.
    /// </summary>
    private void OnRunRequested()
    {
        if (_viewModel.Steps.Count == 0)
        {
            return;
        }

        _running?.Close();
        var macro = _viewModel.BuildMacro();
        var window = new RunWindow(_viewModel.Steps, Layer, _viewModel.DelayScale, BuildLibrary(),
            macroName: _viewModel.Name,
            callable: macro.Callable,
            inputRate: macro.InputRate);
        _running = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_running, window))
            {
                _running = null;
            }
        };

        window.ShowOver(this);
    }

    /// <summary>Opens the run-speed dialog and keeps whatever factor it was given.</summary>
    private async void OnAdjustDelaysRequested()
    {
        if (await DelayScaleWindow.ShowFor(this, _viewModel.DelayScale, _viewModel.Steps) is { } chosen)
        {
            _viewModel.SetDelayScale(chosen);
        }
    }

    /// <summary>
    /// Opens the same window on an existing step and swaps in the result. The catalogue it is
    /// handed includes the actions no longer offered, so a step written before one was retired
    /// still opens.
    /// </summary>
    private async void OnEditStepRequested(MacroStep step)
    {
        // The list the form picks from has to hold the step being edited. A branch of a switch is
        // hidden from the palette — it is only ever written through its switch — and a form that
        // cannot find it would quietly edit it as whichever action came first.
        var actions = ActionCatalog.PlaceableActions;
        if (ActionCatalog.Find(step.Type) is { } definition
            && !actions.Any(action => action.Key == definition.Key))
        {
            actions = [.. actions, definition];
        }

        await ShowActionEditor(step, actions, null, null);
    }

    /// <summary>Confirms the palette's "clear all steps", which cannot be undone.</summary>
    private async void OnClearRequested()
    {
        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Editor.ClearTitle"),
            Strings.Format("Editor.ClearMessage", _viewModel.Steps.Count),
            Strings.Get("Editor.Clear"));

        if (choice == ConfirmChoice.Primary)
        {
            _viewModel.ClearStepsNow();
        }
    }
}
