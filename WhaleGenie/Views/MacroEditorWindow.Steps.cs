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
    // The step rows themselves: folding a block, editing the values a case compares against, and
    // the commands on a step's context menu. Dragging a step stays with the drop geometry.
    /// <summary>Opens or folds the picked block, which is what the arrow keys do in a tree.</summary>
    private void Fold(bool open)
    {
        if (_viewModel.SelectedSteps is [{ } step])
        {
            _viewModel.SetExpanded(step, open);
        }
    }

    /// <summary>True when the point came from inside a row's own content — the card, its text, the badges —
    /// rather than from the indent strip beside it or from a control the row carries.</summary>
    private static bool OnRowContent(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ToggleButton or TextBox)
            {
                return false;
            }

            if (visual is Panel { Name: "RowHeader" })
            {
                return true;
            }

            if (visual is TreeViewItem)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>Adds the next branch of a switch; the new row opens its values for typing.</summary>
    internal void OnAddBranch(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepRow { IsAddRow: true } row })
        {
            _viewModel.AddBranch(row.Step);
        }
    }

    /// <summary>
    /// Turns a branch's values into the line that is typed in their place. A branch is one line of
    /// values — several of them separated by ";" — so this is all the form that field needs.
    /// </summary>
    internal void OnEditCaseValues(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepRow { IsCase: true } row })
        {
            row.BeginEditingValues();
            FocusCaseValues(row);
        }
    }

    /// <summary>
    /// A branch that was just added opens for typing the moment its row exists: the box is built
    /// with the row, so this is where the caret can go without the list being touched again.
    /// </summary>
    internal void OnCaseValueBoxAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox { DataContext: StepRow { IsEditingValues: true } row } box)
        {
            Dispatcher.UIThread.Post(() =>
            {
                box.Focus();
                box.SelectAll();
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Puts the caret in a branch's value box, selecting what is there so it can be replaced.</summary>
    private void FocusCaseValues(StepRow row)
        => Dispatcher.UIThread.Post(() =>
        {
            if (FindCaseValueBox(row) is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        }, DispatcherPriority.Loaded);

    /// <summary>The box a branch row is being typed into, if that row is on screen and open.</summary>
    private TextBox? FindCaseValueBox(StepRow row)
        => _stepList?.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(box => box.Name == "CaseValueBox" && ReferenceEquals(box.DataContext, row));

    /// <summary>
    /// A branch's values are read as text with the names in them replaced, so "1; 2" and "$key" are
    /// the same kind of thing. The button therefore does what the same button does on the step's own
    /// form: put a name where the caret is, or hand the whole line to the expression editor through
    /// the picker's own "write it as an expression" entry.
    /// </summary>
    internal async void OnPickCaseValuesVariable(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepRow { IsCase: true } row })
        {
            return;
        }

        // The pickers are windows of their own, and taking the focus is how a dialog opens: without
        // this the row would read the loss as "the user moved on" and close the line under the
        // dialog, leaving what comes back nowhere to go.
        _pickingCaseValues = true;
        try
        {
            var line = FindCaseValueBox(row);
            var caret = line?.SelectionStart ?? row.CaseValuesText.Length;
            if (await ValuePickerWindow.ShowFor(this, row.CaseValuesText,
                    _viewModel.CollectVariables(), ActionReadMode.WholeValue,
                    startOnFormula: true, startCaret: caret) is not { Chosen: true } written)
            {
                return;
            }

            row.CaseValuesText = FormulaText.Write(written.Value);
        }
        finally
        {
            _pickingCaseValues = false;
            Dispatcher.UIThread.Post(() =>
            {
                if (FindCaseValueBox(row) is { } box)
                {
                    box.Focus();
                    box.CaretIndex = box.Text?.Length ?? 0;
                }
            }, DispatcherPriority.Loaded);
        }
    }

    /// <summary>True while one of the pickers is open over the branch being typed.</summary>
    private bool _pickingCaseValues;

    /// <summary>
    /// Enter keeps the values and leaves the line, Escape puts back what was there. Both end the
    /// edit, because a branch is read as a table: the row is its values, not a form left open.
    /// </summary>
    internal void OnCaseValuesKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: StepRow row } || e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }

        e.Handled = true;
        row.EndEditingValues(keep: e.Key == Key.Enter);
        _stepList?.Focus();
    }

    /// <summary>Leaving the line is keeping what was typed: there is no "cancel" to be had by leaving.</summary>
    internal void OnCaseValuesLostFocus(object? sender, RoutedEventArgs e)
    {
        // A picker opening over the row takes the focus with it; that is not the user leaving.
        if (_pickingCaseValues || sender is not TextBox { DataContext: StepRow row } box
            || !row.IsEditingValues)
        {
            return;
        }

        // So is the row being rebuilt under the box: the list is laid out again whenever a branch
        // is added, and the focus lands on the row's own container on the way. Focus that never
        // left this row — or that has nowhere to go yet — is not the person moving on.
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (_viewModel.IsRebuildingRows
            || (focused is not null
                && box.FindAncestorOfType<TreeViewItem>(true) is { } owner
                && owner.IsVisualAncestorOf(focused)))
        {
            return;
        }

        row.EndEditingValues(keep: true);
    }

    /// <summary>Context-menu commands for the step list, so they are not keyboard-only.</summary>
    internal void OnStepContextEdit(object? sender, RoutedEventArgs e)
        => _viewModel.EditSelectedCommand.Execute(null);

    internal void OnStepContextCopy(object? sender, RoutedEventArgs e) => _ = CopySelectionAsync();

    internal void OnStepContextCut(object? sender, RoutedEventArgs e) => _ = CutSelectionAsync();

    internal void OnStepContextPaste(object? sender, RoutedEventArgs e) => _ = PasteAsync();

    internal void OnStepContextDelete(object? sender, RoutedEventArgs e)
        => _viewModel.DeleteSelectedCommand.Execute(null);

    internal void OnStepContextMoveUp(object? sender, RoutedEventArgs e)
        => _viewModel.MoveSelectedUpCommand.Execute(null);

    internal void OnStepContextMoveDown(object? sender, RoutedEventArgs e)
        => _viewModel.MoveSelectedDownCommand.Execute(null);

    internal void OnStepContextGroup(object? sender, RoutedEventArgs e)
        => _viewModel.GroupSelectedCommand.Execute(null);

    internal void OnStepContextLift(object? sender, RoutedEventArgs e)
        => _viewModel.LiftSelectedCommand.Execute(null);

    internal void OnStepContextToggleEnabled(object? sender, RoutedEventArgs e)
        => _viewModel.ToggleStepEnabledCommand.Execute(null);

    private void ShowDropMarker()
    {
        if (_dropLayer is null || _dropMarker is null)
        {
            return;
        }

        var empty = _viewModel.Roots.Count == 0;
        var height = empty
            ? Math.Max(64, _dropLayer.Bounds.Height)
            : Math.Max(30, _dropMarker.MinHeight);
        var highest = Math.Max(0, _dropLayer.Bounds.Height - height);
        _dropMarker.Height = height;
        Canvas.SetLeft(_dropMarker, _dropMarkerIndent);
        _dropMarker.Width = Math.Max(0, _dropLayer.Bounds.Width - _dropMarkerIndent);
        Canvas.SetTop(_dropMarker, empty
            ? 0
            : Math.Clamp(_dropMarkerY - (height / 2), 0, highest));
        _dropMarker.Classes.Set("Rejected", !_dropAllowed);
        if (_dropHint is not null)
        {
            _dropHint.Text = _dropRefusal.Length > 0
                ? _dropRefusal
                : Strings.Get(_dropHintKey);
        }

        _dropMarker.IsVisible = true;
    }

    /// <summary>
    /// Puts the tree's own highlight on exactly the nodes the editor has picked. The tree keeps
    /// a highlight per node and the editor keeps one per step, and this is the one place the two
    /// are made to agree; without it, a rebuild would leave a row looking picked that is not.
    /// </summary>
    private static void SyncSelection(TreeView tree, IReadOnlyList<object> wanted)
    {
        if (tree.SelectedItems is not { } selection)
        {
            return;
        }

        for (var index = selection.Count - 1; index >= 0; index--)
        {
            if (selection[index] is not { } item || !wanted.Contains(item))
            {
                selection.RemoveAt(index);
            }
        }

        foreach (var item in wanted)
        {
            if (!selection.Contains(item))
            {
                selection.Add(item);
            }
        }
    }

    /// <summary>The row a pointer event happened on, or <c>null</c> when it missed them all.</summary>
    private static StepRow? RowUnder(object? source)
        => source is Visual visual
            ? visual.FindAncestorOfType<TreeViewItem>(true)?.DataContext as StepRow
            : null;

    /// <summary>True when the event landed on a button, which handles its own presses.</summary>
    private static bool IsOnButton(object? source)
        => source is Visual visual && visual.FindAncestorOfType<Button>(true) is not null;

    /// <summary>True when the event landed in a text box, which owns its own presses and keys.</summary>
    private static bool IsOnText(object? source)
        => source is Visual visual && visual.FindAncestorOfType<TextBox>(true) is not null;

    /// <summary>True when the event landed on the list's scrollbar rather than on its rows.</summary>
    private static bool IsOnScrollbar(object? source)
        => source is Visual visual
            && visual.FindAncestorOfType<Avalonia.Controls.Primitives.ScrollBar>(true) is not null;
}
