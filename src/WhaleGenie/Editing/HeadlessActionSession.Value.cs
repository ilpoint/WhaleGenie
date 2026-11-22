using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

// Conditions, values and the row cells of one action draft. Everything writes the same
// StepParameterViewModel the dialog draws, so the window and the form never hold two copies.
internal sealed partial class HeadlessActionSession
{
    private HeadlessValueSession? _valueSession;

    public EditorActionFormSnapshot AddConditionRow(string name, int[] path)
    {
        var editor = Condition(name);
        editor.AddRow(Group(editor, path));
        return Snapshot();
    }

    public EditorActionFormSnapshot AddConditionGroup(string name, int[] path)
    {
        var editor = Condition(name);
        editor.AddGroup(Group(editor, path));
        return Snapshot();
    }

    public EditorActionFormSnapshot RemoveConditionNode(string name, int[] path)
    {
        var editor = Condition(name);
        editor.Remove(Node(editor, path));
        return Snapshot();
    }

    public EditorActionFormSnapshot SetConditionRow(string name, int[] path, string field, string value)
    {
        var editor = Condition(name);
        var node = Node(editor, path);
        switch (field)
        {
            case "left":
                editor.WriteExpression(node, right: false, Formula.Text(value));
                break;
            case "right":
                editor.WriteExpression(node, right: true, Formula.Text(value));
                break;
            case "compare":
                node.Compare = Enum.TryParse<ConditionCompare>(value, ignoreCase: true, out var compare)
                    ? compare
                    : node.Compare;
                break;
            default:
                throw new InvalidOperationException("unknown condition field " + field);
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot SetConditionJoin(string name, int[] path, string value)
    {
        var editor = Condition(name);
        var node = path.Length == 0 ? editor.Root : Node(editor, path);
        node.Join = Enum.TryParse<Join>(value, ignoreCase: true, out var join) ? join : node.Join;
        return Snapshot();
    }

    public EditorValueSnapshot OpenValue(string name)
    {
        var field = Field(name);
        var picker = new ValuePickerViewModel(field.CurrentShape, field.Variables,
            field.Definition.ValueForm, field.Label, field.AllowsNewName);
        _valueSession = new HeadlessValueSession(picker, value => field.WriteValue(value), () => { });
        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot OpenConditionValue(string name, int[] path, string field)
    {
        var editor = Condition(name);
        var node = Node(editor, path);
        var right = string.Equals(field, "right", StringComparison.Ordinal);
        var seed = right ? node.RightNode : node.LeftNode;
        var picker = new ValuePickerViewModel(seed, editor.Variables, ActionValueForm.Value,
            name + " · " + (right ? WhaleGenie.Localization.Strings.Get("Editor.ConditionRight")
                : WhaleGenie.Localization.Strings.Get("Editor.ConditionLeft")));
        _valueSession = new HeadlessValueSession(picker,
            value => editor.WriteExpression(node, right, value), () => { });
        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot OpenRowValue(string name, int index, string column)
    {
        var field = Field(name);
        var (seed, write) = RowCell(field, index, column);
        var picker = new ValuePickerViewModel(seed, field.Variables, ActionValueForm.Value,
            field.Label + " · " + column);
        _valueSession = new HeadlessValueSession(picker, write, () => { });
        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot ValueState()
        => _valueSession?.Snapshot()
           ?? throw new InvalidOperationException("no value field is open");

    public EditorValueSnapshot ValueCommand(ValueEditorCommand command)
        => _valueSession?.Execute(command)
           ?? throw new InvalidOperationException("no value field is open");

    public ValueEditorOutcome ConfirmValue()
    {
        var session = _valueSession ?? throw new InvalidOperationException("no value field is open");
        var outcome = session.Confirm();
        if (outcome.Closed)
        {
            _valueSession = null;
        }

        return outcome;
    }

    public ValueEditorOutcome CancelValue()
    {
        var session = _valueSession ?? throw new InvalidOperationException("no value field is open");
        var outcome = session.Cancel();
        _valueSession = null;
        return outcome;
    }

    public void CloseValueEditor() => _valueSession = null;

    private ConditionEditorViewModel Condition(string name)
        => Field(name).ConditionEditor
           ?? throw new InvalidOperationException("the field is not a condition");

    private static ConditionNodeViewModel Group(ConditionEditorViewModel editor, int[] path)
        => path.Length == 0 ? editor.Root : Node(editor, path);

    private static ConditionNodeViewModel Node(ConditionEditorViewModel editor, int[] path)
    {
        var node = editor.Root;
        foreach (var index in path)
        {
            if (index < 0 || index >= node.Children.Count)
            {
                throw new InvalidOperationException("the condition has no node at this path");
            }

            node = node.Children[index];
        }

        return node;
    }

    private static (ExpressionNode Seed, Action<ExpressionNode> Write) RowCell(
        StepParameterViewModel field, int index, string column)
    {
        switch (field.Definition.Kind)
        {
            case ActionParameterKind.Region:
                {
                    var row = At(field.Regions, index);
                    var cell = column switch
                    {
                        "x" => row.X,
                        "y" => row.Y,
                        "width" => row.Width,
                        "height" => row.Height,
                        _ => throw new InvalidOperationException("unknown region column " + column),
                    };
                    return (cell.Seed, cell.Write);
                }
            case ActionParameterKind.Images:
                return TextCell(At(field.Pictures, index).Text,
                    value => At(field.Pictures, index).Text = Expression.Render(value));
            case ActionParameterKind.ColourPoints:
                return ColourPointCell(At(field.ColourPointRows, index), column);
            case ActionParameterKind.Color when field.IsColourRows:
                {
                    var row = At(field.ColourRows, index);
                    return column switch
                    {
                        "colour" => TextCell(row.Colour, value => row.Colour = Expression.Render(value)),
                        "tolerance" => TextCell(row.Tolerance, value => row.Tolerance = Expression.Render(value)),
                        _ => throw new InvalidOperationException("unknown colour column " + column),
                    };
                }
            case ActionParameterKind.KeySequence:
                {
                    var row = At(field.KeyRows, index);
                    return column switch
                    {
                        "keys" => TextCell(row.Keys, value => row.Keys = Expression.Render(value)),
                        "hold" => TextCell(NumberText(row.HoldMs),
                            value => row.HoldMs = decimal.TryParse(Expression.Render(value),
                                System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture, out var number)
                                ? number
                                : null),
                        "gap" => TextCell(NumberText(row.GapMs),
                            value => row.GapMs = decimal.TryParse(Expression.Render(value),
                                System.Globalization.NumberStyles.Number,
                                System.Globalization.CultureInfo.InvariantCulture, out var number)
                                ? number
                                : null),
                        _ => throw new InvalidOperationException("unknown key column " + column),
                    };
                }
            case ActionParameterKind.Narrow:
                {
                    var row = At(field.NarrowRows, index);
                    return TextCell(row.Value, value => row.Value = Expression.Render(value));
                }
            default:
                throw new InvalidOperationException("the field has no value window");
        }
    }

    private static (ExpressionNode Seed, Action<ExpressionNode> Write) ColourPointCell(
        ColourPointRowViewModel row, string column)
        => column switch
        {
            "dx" => TextCell(row.Dx, value => row.Dx = Expression.Render(value)),
            "dy" => TextCell(row.Dy, value => row.Dy = Expression.Render(value)),
            "colour" => TextCell(row.Colour, value => row.Colour = Expression.Render(value)),
            "tolerance" => TextCell(row.Tolerance, value => row.Tolerance = Expression.Render(value)),
            _ => throw new InvalidOperationException("unknown colour column " + column),
        };

    private static (ExpressionNode Seed, Action<ExpressionNode> Write) TextCell(string text,
        Action<ExpressionNode> write)
        => (Formula.Text(text), write);

    private static string NumberText(decimal? value)
        => value is { } number
            ? number.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
