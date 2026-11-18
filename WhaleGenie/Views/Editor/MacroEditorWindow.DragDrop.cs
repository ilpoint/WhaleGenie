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
using WhaleGenie.Converters;
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
    // Dragging a step and dropping an action: the pointer gesture, the seam it would land on, and
    // what an action dropped there becomes. The row drawing itself stays in the steps partial.
    /// <summary>Remembers where a press landed, so a plain click never turns into a drag.</summary>
    private void OnStepPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        var onButton = IsOnButton(e.Source);

        // A text box a row carries owns its own presses: clicking into it is putting the caret
        // somewhere, not picking the row up as a drag or clearing the selection around it.
        var onText = IsOnText(e.Source);
        var row = onButton || onText ? null : RowUnder(e.Source);

        // The second press of a double click opens the row it landed on. Doing it here rather than
        // on the double-tap keeps it independent of what selection did with the first click, and
        // marking the press handled is also what stops the tree from folding the block that holds
        // the row: opening a step must not close the block it was found in.
        if (e.ClickCount == 2 && row is { } twice && (twice.IsStep || twice.IsCase)
            && OnRowContent(e.Source))
        {
            e.Handled = true;
            _pressedRow = null;
            Dispatcher.UIThread.Post(() => OnEditStepRequested(twice.Step));
            return;
        }

        // A right-click on a row that is not part of the selection picks that row first, so the
        // context menu acts on what was clicked rather than on something picked earlier.
        if (point.Properties.IsRightButtonPressed)
        {
            if (row is { IsStep: true } && !_viewModel.SelectedSteps.Contains(row.Step))
            {
                _viewModel.SetSelection([row.Step]);
                _viewModel.RefreshSelection();
            }

            _pressedRow = null;
            return;
        }

        var pressed = point.Properties.IsLeftButtonPressed;

        // A press on a button inside a row belongs to that button: folding a block open, or
        // asking for a step inside one, must not turn into picking the row up as a drag.
        _pressedRow = pressed ? row : null;

        // A press on the empty part of the list — the space after the last row — is the user
        // saying "none of these", so the selection goes, the way it does in any other list. The
        // scrollbar is not empty space.
        if (pressed && row is null && !onButton && !onText && !IsOnScrollbar(e.Source))
        {
            _viewModel.SetSelection([]);

            // The list keeps a highlight of its own, so clearing the editor alone leaves the row
            // looking picked: the list has to be told to let go as well.
            _viewModel.RefreshSelection();
        }

        _dragOrigin = e.GetPosition(this);
        _draggingSteps = false;
        _dropList = null;
        _dropIndex = -1;
    }

    /// <summary>Starts the drag once the pointer has moved far enough, then tracks the drop row.</summary>
    private void OnStepPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedRow is null)
        {
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            EndStepDrag();
            return;
        }

        if (!_draggingSteps)
        {
            var moved = e.GetPosition(this);

            // A few pixels of slack, so a shaky click is still a click.
            if (Math.Abs(moved.X - _dragOrigin.X) < 4 && Math.Abs(moved.Y - _dragOrigin.Y) < 4)
            {
                return;
            }

            // Dragging a row that is already part of a multiple selection moves them all.
            if (_stepList is { } list && !_viewModel.SelectedSteps.Contains(_pressedRow.Step))
            {
                list.SelectedItem = _pressedRow;
            }

            _draggingSteps = true;
            e.Pointer.Capture(_stepList);
        }

        UpdateDropSlot(e);
    }

    /// <summary>Drops the block where the marker points.</summary>
    private void OnStepPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        // A press that never turned into a drag belongs to whatever it landed on. Handing the
        // capture back here would take it off the row button that took it, and a button that loses
        // its capture on the way up never reports a click — which is how the fold arrow and the
        // "add a step" button came to do nothing at all.
        if (!_draggingSteps)
        {
            EndStepDrag();
            return;
        }

        // The state is read and cleared before the capture goes back, because losing the
        // capture asks for the drag to end and would wipe the drop seam out from under us.
        var list = _dropList;
        var index = _dropIndex;
        var allowed = _dropAllowed;
        var refusal = _dropRefusal;
        EndStepDrag();
        e.Pointer.Capture(null);

        if (!allowed)
        {
            _viewModel.ReportPlacementRefusal(refusal);
            return;
        }

        if (list is not null && index >= 0)
        {
            _viewModel.MoveSelectionTo(list, index);
        }
    }

    private void OnStepPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        => EndStepDrag();

    private void EndStepDrag()
    {
        _pressedRow = null;
        _draggingSteps = false;
        _dropList = null;
        _dropIndex = -1;
        _dropAllowed = true;
        _dropRefusal = string.Empty;

        if (_dropMarker is not null)
        {
            _dropMarker.IsVisible = false;
        }
    }

    /// <summary>Works out where the dragged block would land, and moves the marker onto that seam.</summary>
    private void UpdateDropSlot(PointerEventArgs e)
        => UpdateDropAt(e.GetPosition(_stepList), actionKey: null);

    /// <summary>
    /// The same question asked without a pointer, answered with what the marker would say: which
    /// list the steps land in, at what position, and whether the drop is allowed. A drag cannot be
    /// driven from a check, and the seams a tree offers are worth one.
    /// </summary>
    internal (IList<MacroStep>? Into, int Index, bool Allowed) DropTargetAt(Point point, string? actionKey)
    {
        UpdateDropAt(point, actionKey);
        return (_dropList, _dropIndex, _dropAllowed);
    }

    /// <summary>
    /// A tree answers "where does this go" with a list and a position rather than with a row
    /// number: the same place in the document can be reached by dropping above the step below it
    /// or below the step above it, and the list is what both answers have in common.
    /// </summary>
    private void UpdateDropAt(Point point, string? actionKey = null)
    {
        if (_stepList is null)
        {
            return;
        }

        _dropAllowed = true;
        _dropRefusal = string.Empty;
        _dropList = null;
        _dropIndex = -1;
        _dropMarkerY = 0;
        _dropMarkerIndent = 0;
        _dropHintKey = "Editor.DropAtPosition";

        var boxes = HeaderBoxes();

        // Nothing on screen: the macro is empty, so the only seam is the one at its top.
        if (boxes.Count == 0)
        {
            Place(_viewModel.Steps, 0, 0, 0, "Editor.DropIntoEmptyList", actionKey);
            return;
        }

        // The top edge of the first row is the start of the macro: a block that is first still has
        // a place in front of it, and a few pixels there are what says so.
        if (point.Y < boxes[0].Top + 6)
        {
            Place(_viewModel.Steps, 0, boxes[0].Top, 0, "Editor.DropBeforeStep", actionKey);
            return;
        }

        // The space below the last row is the end of the macro: a pointer down there is past
        // everything, so that is what it means rather than "after whichever block happened to be
        // last".
        var last = boxes[^1];
        if (point.Y >= last.Top + last.Height)
        {
            // 最后一行是块时句子要说"这一块之后"：行上画的是块，叫它"一步"读起来像另一个位置。
            Place(_viewModel.Steps, _viewModel.Steps.Count, last.Top + last.Height, 0,
                last.Row.IsBlock ? "Editor.DropAfterBlock" : "Editor.DropAfterStep", actionKey);
            return;
        }

        // The gaps between rows belong to the row above them, so a pointer never falls into a
        // height no row answers for.
        var at = 0;
        for (var candidate = 0; candidate < boxes.Count; candidate++)
        {
            if (point.Y >= boxes[candidate].Top)
            {
                at = candidate;
            }
        }

        var box = boxes[at];
        var row = box.Row;
        var top = box.Top;
        var bottom = top + box.Height;

        // The foot of a switch's branch table is the end of the branch list: a branch dragged onto
        // it is saying "put it last", which is the same thing the row itself offers to do.
        if (row.IsAddRow && row.List is { } table)
        {
            Place(table.Steps, table.Steps.Count, bottom, TreeIndent.Of(row.Depth),
                table.Steps.Count == 0 ? "Editor.DropIntoEmptyList" : "Editor.DropAtBlockEnd",
                actionKey);
            return;
        }

        // A body row is one list, and the whole of it means the end of that list: a pointer over
        // "body steps" is saying "put it in there", and the end is where a person's next step
        // goes. Landing it at the top is what made an empty body feel like it swallowed the drop.
        //
        // The band sits one column in while the list it points into is on screen, and on the row's
        // own column when it is folded: the steps it would land beside are not drawn, and a band
        // indented into a column with no rows in it reads as a place that is not there.
        if (row.IsSection && row.List is { } section)
        {
            Place(section.Steps, section.Steps.Count, BottomOf(row, boxes),
                TreeIndent.Of(row.IsExpanded ? row.Depth + 1 : row.Depth),
                section.Steps.Count == 0 ? "Editor.DropIntoEmptyList" : "Editor.DropAtBlockEnd",
                actionKey);
            return;
        }

        // An open block that runs one list is the same promise one level up: the whole row means
        // "into it, at the end". A block that runs several lists leaves "inside" to its body rows,
        // because "inside" there would not say which.
        //
        // A folded block promises nothing of the sort: none of its steps is on screen, so there is
        // no place inside it to point at. The whole of its row then means what the row of any other
        // step means — before it, or after it — and the band is drawn where that step would land.
        // Adding inside a folded block is still done by picking the block and asking for a step,
        // which opens the block and puts the step at the end of its body.
        if (row.IsExpanded && row.IsBlock && row.Step.StepLists.Count() == 1 && row.Body is { } body)
        {
            var tail = row.Children.FirstOrDefault(child => child.IsSection);
            Place(body.Steps, body.Steps.Count,
                tail is null ? bottom : BottomOf(tail, boxes), TreeIndent.Of(row.Depth + 1),
                body.Steps.Count == 0 ? "Editor.DropIntoEmptyList" : "Editor.DropAtBlockEnd",
                actionKey);
            return;
        }

        var before = point.Y < top + (box.Height / 2);
        var (list, index) = before ? Before(row) : After(row);
        Place(list, index, before ? top : bottom, TreeIndent.Of(row.Depth),
            row.IsBlock ? (before ? "Editor.DropBeforeBlock" : "Editor.DropAfterBlock")
                        : (before ? "Editor.DropBeforeStep" : "Editor.DropAfterStep"),
            actionKey);
    }

    /// <summary>Records the seam under the pointer and draws the marker on it.</summary>
    private void Place(IList<MacroStep> list, int index, double y, double indent,
        string hintKey, string? actionKey)
    {
        _dropList = list;
        _dropIndex = Math.Clamp(index, 0, list.Count);
        _dropMarkerY = y;
        _dropMarkerIndent = indent;
        _dropHintKey = hintKey;
        EvaluateDrop(list, _dropIndex, actionKey);
        ShowDropMarker();
    }

    /// <summary>The list a row sits in, which is the macro's own list for a step at the top.</summary>
    private IList<MacroStep> StepsOf(StepRow row)
        => (IList<MacroStep>?)row.List?.Steps ?? _viewModel.Steps;

    /// <summary>The seam in front of a row: its own list, at its own place.</summary>
    private (IList<MacroStep> List, int Index) Before(StepRow row)
        => (StepsOf(row), Math.Max(0, row.Index));

    /// <summary>
    /// The seam behind a row. Behind a block that is the next step of the list it sits in rather
    /// than the end of its body: the body's end is the lower half of the block's own row.
    /// </summary>
    private (IList<MacroStep> List, int Index) After(StepRow row)
    {
        var list = StepsOf(row);
        return (list, Math.Clamp(row.Index + 1, 0, list.Count));
    }

    /// <summary>Where the last row inside a section is drawn, so "at the end of it" can be shown.</summary>
    private static double BottomOf(StepRow section, IReadOnlyList<RowBox> boxes)
    {
        var lastIndex = LastRowIndex(section);
        var last = boxes.LastOrDefault(box => box.Row.RowIndex <= lastIndex);
        return last.Row is null ? 0 : last.Top + last.Height;
    }

    private static int LastRowIndex(StepRow row)
        => row.Children.Count == 0 ? row.RowIndex : Math.Max(row.RowIndex, LastRowIndex(row.Children[^1]));

    /// <summary>
    /// The row headers on screen, top to bottom, as the tree actually laid them out.
    /// </summary>
    /// <remarks>
    /// 折叠一个块只是把子行那个 presenter 藏起来：那些行还在视觉树里，`Bounds` 也还是折叠前
    /// 那一份。只看"有没有高度"会把它们算成屏幕上的位置，于是折叠之后，指针走到原来放循环体的
    /// 那一段仍会命中一行看不见的体行，插入带便回到已经收起来的那一层。屏幕上有没有这一行，
    /// 问的是它此刻可不可见。
    /// </remarks>
    private List<RowBox> HeaderBoxes()
        => _stepList is null
            ? []
            : [.. _stepList.GetVisualDescendants()
                .OfType<Panel>()
                .Where(panel => panel.Name == "RowHeader" && panel.IsEffectivelyVisible)
                .Select(panel => (
                    Panel: panel,
                    Row: panel.DataContext as StepRow,
                    Top: panel.TranslatePoint(new Point(0, 0), _stepList)?.Y ?? 0))
                .Where(candidate => candidate.Row is { } && candidate.Panel.Bounds.Height > 0)
                .Select(candidate => new RowBox(
                    candidate.Row!, candidate.Top, candidate.Panel.Bounds.Height))
                .OrderBy(box => box.Top)];

    /// <summary>One row header: what it stands for, where it is drawn, and how tall it is.</summary>
    private readonly record struct RowBox(StepRow Row, double Top, double Height)
    {
        /// <summary>
        /// The first entry of the default value answers "no row here". A struct cannot be null, so
        /// the one thing a caller checks is that it is looking at a real row.
        /// </summary>
        public bool Exists => Row is not null;
    }

    /// <summary>Asks the editor whether the seam under the pointer accepts the proposed step(s).</summary>
    private void EvaluateDrop(IList<MacroStep> list, int index, string? actionKey)
    {
        var refusal = actionKey is null
            ? _viewModel.MoveSelectionRefusalAt(list, index)
            : _viewModel.PlacementRefusalAt(list, index, [new MacroStep { Type = actionKey }]);
        _dropAllowed = refusal is null;
        _dropRefusal = refusal ?? string.Empty;
    }

    private void OnActionDragOver(object? sender, DragEventArgs e)
    {
        if (_stepList is null
            || ActionDragPayload.Read(e.DataTransfer) is not { } payload)
        {
            e.DragEffects = DragDropEffects.None;
            if (_dropMarker is not null)
            {
                _dropMarker.IsVisible = false;
            }

            return;
        }

        var actionKey = payload.Kind == ActionPaletteCardKind.Action
            ? payload.Key
            : "control.runMacro";
        UpdateDropAt(e.GetPosition(_stepList), actionKey);
        e.DragEffects = _dropAllowed ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnActionDragLeave(object? sender, DragEventArgs e)
    {
        _dropList = null;
        _dropIndex = -1;
        if (_dropMarker is not null)
        {
            _dropMarker.IsVisible = false;
        }
    }

    private async void OnActionDropped(object? sender, DragEventArgs e)
    {
        if (ActionDragPayload.Read(e.DataTransfer) is not { } payload)
        {
            return;
        }

        var actionKey = payload.Kind == ActionPaletteCardKind.Action
            ? payload.Key
            : "control.runMacro";
        UpdateDropAt(e.GetPosition(_stepList!), actionKey);
        var list = _dropList;
        var index = _dropIndex;
        var allowed = _dropAllowed;
        var refusal = _dropRefusal;
        var editingPage = _viewModel;
        EndStepDrag();

        if (!allowed)
        {
            _viewModel.ReportPlacementRefusal(refusal);
            return;
        }

        if (list is null || index < 0)
        {
            return;
        }

        _viewModel.PrepareInsertionAt(list, index);
        if (!_viewModel.CanPlace(actionKey))
        {
            _viewModel.ClearPendingInsertion();
            _viewModel.ReportPlacementRefusal(
                Strings.Format("Editor.PlaceUnavailable", actionKey));
            return;
        }

        string? macroName = null;
        _session.Pending = null;
        if (payload.Kind == ActionPaletteCardKind.SubMacro)
        {
            // A submacro no longer has to be open in a page of its own to be called: it is in the
            // document, and the dock that offered it is the document's own list.
            var sub = DocumentSubMacros().FirstOrDefault(macro =>
                string.Equals(macro.Name, payload.Key, StringComparison.OrdinalIgnoreCase));
            macroName = sub is null ? null : CallNameFor(sub);
            if (macroName is null)
            {
                _viewModel.ClearPendingInsertion();
                return;
            }
        }
        else if (payload.Kind == ActionPaletteCardKind.PublicMacro)
        {
            macroName = StagePublicSubMacro(payload.Key, payload.Version);
            if (macroName is null)
            {
                _viewModel.ClearPendingInsertion();
                return;
            }
        }

        await ShowActionEditor(null, _viewModel.AddableActions, actionKey, macroName);
        _viewModel = editingPage;
        DataContext = editingPage;
    }

    private string? StagePublicSubMacro(string publicId, int version)
    {
        var existing = _documentPages.FirstOrDefault(page => !page.IsCurrent
            && page.Editor.Origin is { } origin
            && string.Equals(origin.Id, publicId, StringComparison.Ordinal)
            && origin.Version == version);
        if (existing is not null)
        {
            return existing.Editor.Name;
        }

        var entry = _publicMacros.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, publicId, StringComparison.Ordinal)
            && candidate.Version == version);
        if (entry is null)
        {
            return null;
        }

        var copy = PublicMacroLibrary.CopyToProject(entry, _session.AssetFolder);
        copy.Name = _documentPages.UniqueName(entry.Macro.Name);
        var editor = new MacroEditorViewModel { IsSubMacro = true };
        if (_devices is { } devices)
        {
            editor.Windows = devices.Windows;
        }

        editor.Documents = _session.Documents;
        editor.LoadFrom(copy, []);
        editor.SubMacros.Clear();
        editor.IsDirty = true;
        WatchChildEditor(editor);
        var page = new MacroDocumentPageViewModel(editor, isCurrent: false, copy);
        _session.Pending = new PendingSubMacro(editor, page);
        return editor.Name;
    }

    /// <summary>Layer the drag marker is drawn on, over the step list.</summary>
    private Canvas? _dropLayer;
    private Border? _dropMarker;
    private TextBlock? _dropHint;
    private string _dropHintKey = "Editor.DropAtPosition";

    /// <summary>Where the pointer went down, so a click can be told apart from a drag.</summary>
    private Point _dragOrigin;

    /// <summary>The row under the pointer when it went down; null when the press missed the rows.</summary>
    private StepRow? _pressedRow;

    private bool _draggingSteps;

    /// <summary>The list the dragged steps would land in, and where in it.</summary>
    private IList<MacroStep>? _dropList;
    private int _dropIndex = -1;

    /// <summary>
    /// True while the add-action window is open, so a double-click that lands twice does not stack
    /// two forms on top of each other. The window is modal; a second one would sit on the first.
    /// </summary>
    private bool _actionEditorOpen;

    private double _dropMarkerY;

    /// <summary>How far the drop line is indented, which is what says "inside this block".</summary>
    private double _dropMarkerIndent;

    /// <summary>Whether the seam under the pointer accepts what is being dragged.</summary>
    private bool _dropAllowed = true;

    /// <summary>Why that seam refuses it, empty while the drop is allowed.</summary>
    private string _dropRefusal = string.Empty;
}
