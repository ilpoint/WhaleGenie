using System;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>One command from the token band or one of its panels.</summary>
internal sealed record ValueEditorCommand(
    string Action,
    string? Token = null,
    string? Name = null,
    string? Sign = null,
    string? Group = null,
    string? Text = null,
    int[]? Path = null,
    int[]? Target = null,
    bool? After = null,
    bool? Number = null,
    int? Direction = null,
    int? At = null,
    double? Width = null,
    bool? Append = null);

/// <summary>The answer of the modal: whether it closed, and whether the field was written.</summary>
internal sealed record ValueEditorOutcome(bool Closed, bool Applied, EditorValueSnapshot Snapshot);

/// <summary>
/// The headless form of the restored value picker. The C# <see cref="ValuePickerViewModel"/> still
/// owns the token tree, caret, wall and functions; this class translates the page's small command
/// vocabulary into its methods and writes the confirmed shape back through a callback. No formula
/// is ever parsed from the page.
/// </summary>
internal sealed class HeadlessValueSession
{
    private readonly ValuePickerViewModel _picker;
    private readonly Action<ExpressionNode> _write;
    private readonly Action _changed;
    private ValuePick? _result;

    public HeadlessValueSession(ValuePickerViewModel picker, Action<ExpressionNode> write,
        Action changed)
    {
        _picker = picker;
        _write = write;
        _changed = changed;
        _picker.CloseRequested += pick => _result = pick;
        _picker.RefreshStacking(900);
    }

    public string Target => _picker.Target;

    public EditorValueSnapshot Snapshot() => EditorValueSnapshotFactory.Build(_picker);

    public EditorValueSnapshot Execute(ValueEditorCommand command)
    {
        switch (command.Action)
        {
            case "place":
                _picker.Place(command.Token ?? string.Empty);
                break;
            case "function":
                if (_picker.Functions.FirstOrDefault(function =>
                        string.Equals(function.Name, command.Name, StringComparison.Ordinal)) is { } function)
                {
                    _picker.SelectedFunction = function;
                    _picker.PlaceCall(function.Name);
                }

                break;
            case "variable":
                if (_picker.Wall.Shown.FirstOrDefault(choice =>
                        string.Equals(choice.Name, command.Name, StringComparison.Ordinal)) is { } choice)
                {
                    _picker.Wall.Looking = choice;
                    if (!_picker.IsNameMode)
                    {
                        _picker.Place(choice.Name);
                    }
                }

                break;
            case "wrapGroup":
                _picker.WrapGroup();
                break;
            case "wrapList":
                _picker.WrapList();
                break;
            case "changeOperator":
                if (Find(command.Path) is { } operatorNode)
                {
                    _picker.ChangeOperator(operatorNode, command.Sign ?? string.Empty);
                }

                break;
            case "remove":
                if (Find(command.Path) is { } removeNode)
                {
                    _picker.RemoveArgument(removeNode);
                }

                break;
            case "cutBefore":
                _picker.CutBeforeCaret();
                break;
            case "cutAfter":
                _picker.CutAfterCaret();
                break;
            case "select":
                _picker.Select(Find(command.Path));
                break;
            case "caret":
                if (command.At is { } at)
                {
                    _picker.SetCaret(at);
                }
                else if (command.Direction is { } direction)
                {
                    _picker.StepCaret(direction);
                }

                break;
            case "typingMode":
                _picker.SetTypingMode(command.Number == true);
                break;
            case "textBody":
                _picker.TextBody = command.Text ?? string.Empty;
                break;
            case "typingText":
                if (command.Text is { } typing)
                {
                    if (command.Append == true)
                    {
                        _picker.AppendTyped(typing);
                    }
                    else
                    {
                        _picker.TypingText = typing;
                    }
                }

                break;
            case "insertTyped":
                if (_picker.CanInsertTyped)
                {
                    _picker.PutTyped();
                    _picker.ClearTyped();
                }

                break;
            case "undo":
                _picker.Undo();
                break;
            case "redo":
                _picker.Redo();
                break;
            case "clear":
                _picker.Clear();
                break;
            case "wallFilter":
                if (_picker.Wall.Filters.FirstOrDefault(filter =>
                        string.Equals(filter.Group.ToString(), command.Group,
                            StringComparison.OrdinalIgnoreCase)) is { } filter)
                {
                    _picker.Wall.SelectedFilter = filter;
                }

                break;
            case "wallSearch":
                _picker.Wall.SearchText = command.Text ?? string.Empty;
                break;
            case "functionGroup":
                if (_picker.FunctionGroups.FirstOrDefault(group =>
                        string.Equals(group.Group, command.Group,
                            StringComparison.OrdinalIgnoreCase)) is { } functionGroup)
                {
                    _picker.SelectedFunctionGroup = functionGroup;
                }

                break;
            case "functionSearch":
                _picker.FunctionQuery = command.Text ?? string.Empty;
                break;
            case "resize":
                if (command.Width is { } width)
                {
                    _picker.RefreshStacking(width);
                }

                break;
            case "move":
                if (Find(command.Path) is { } moved
                    && Find(command.Target) is { } target)
                {
                    _picker.MoveOnto(moved, target, command.After == true);
                }

                break;
        }

        return Snapshot();
    }

    public ValueEditorOutcome Confirm()
    {
        _picker.Confirm();
        if (_result is { Chosen: true } pick)
        {
            _write(pick.Value);
            _changed();
            return new ValueEditorOutcome(true, true, Snapshot());
        }

        return new ValueEditorOutcome(false, false, Snapshot());
    }

    public ValueEditorOutcome Cancel()
    {
        _picker.Cancel();
        return new ValueEditorOutcome(true, false, Snapshot());
    }

    private SlotNode? Find(int[]? path)
    {
        if (path is null)
        {
            return null;
        }

        var wanted = new NodePath(path);
        return _picker.Recipe?.Walk().FirstOrDefault(node =>
            node.Path?.Equals(wanted) == true);
    }
}
