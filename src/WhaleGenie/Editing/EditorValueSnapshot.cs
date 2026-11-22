using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>
/// A serialisable picture of the restored value picker. The C# view model owns every token, caret
/// and wall state; the page only draws this snapshot and sends commands back.
/// </summary>
internal sealed record EditorValueSnapshot(
    string Mode,
    string Title,
    string Hint,
    string Target,
    string TargetLine,
    bool ShowsBand,
    bool ShowsFormulaMaterials,
    int PanelSpan,
    bool IsTextMode,
    bool IsRunMode,
    bool IsValueMode,
    bool IsNameMode,
    EditorValueNode? Recipe,
    int Caret,
    int SpanEnd,
    int[] CaretPath,
    bool CaretAfter,
    int[]? SelectedPath,
    bool IsValueEmpty,
    string EmptyBandHint,
    string StructureHint,
    string Text,
    string Note,
    bool NoteIsProblem,
    string Saying,
    bool Reads,
    bool HasSaying,
    bool CanConfirm,
    bool CanUndo,
    bool CanRedo,
    bool CanClear,
    string TextBody,
    bool IsTypingNumber,
    string TypingText,
    string TypingPlaceholder,
    bool CanInsertTyped,
    string TypingNote,
    bool HasTypingNote,
    string FunctionQuery,
    string? SelectedFunctionGroup,
    bool IsAllFunctions,
    bool HasChosenFunction,
    bool HasChosenVariable,
    bool AllowNewName,
    string NewName,
    bool CanCreateName,
    string NewNameLine,
    IReadOnlyList<EditorValueOperator> Operators,
    IReadOnlyList<EditorValueToken> Values,
    IReadOnlyList<EditorValueFunctionGroup> FunctionGroups,
    IReadOnlyList<EditorValueFunction> Functions,
    EditorValueWall Wall);

internal sealed record EditorValueNode(
    string Kind,
    int[]? Path,
    int Start,
    int Length,
    string Text,
    string Operator,
    string Tone,
    string Name,
    string Before,
    bool IsName,
    bool IsGhost,
    bool IsRequired,
    bool IsEmpty,
    bool IsSelected,
    bool HasProblem,
    string ProblemHint,
    string? HoverHint,
    bool CanRemove,
    bool HasNoArguments,
    bool HasNoItems,
    bool Stacked,
    bool Compact,
    string ArgumentLabel,
    string ArgumentFlag,
    int ArgumentIndex,
    bool ArgumentOptional,
    bool ArgumentRequired,
    IReadOnlyList<EditorValueNode> Children,
    EditorValueNode? Ghost);

internal sealed record EditorValueOperator(string Token, string Insert, string Hint, string Tone, bool Enabled);

internal sealed record EditorValueToken(string Text, string Insert);

internal sealed record EditorValueFunctionGroup(string Group, string Label, bool Selected);

internal sealed record EditorValueFunction(
    string Name,
    string Tag,
    string Heading,
    string WrittenAs,
    string ArityNote,
    string Hint,
    string Group,
    string Insert,
    IReadOnlyList<EditorValueFunctionArgument> Arguments);

internal sealed record EditorValueFunctionArgument(string Label, string Hint);

internal sealed record EditorValueVariableFilter(string Group, string Label, string Hint, bool Selected);

internal sealed record EditorValueVariable(
    string Name,
    string Display,
    string? Type,
    string Group,
    string Origin,
    string Source,
    string Description,
    bool Persistent,
    string Tone,
    string Details);

internal sealed record EditorValueWall(
    IReadOnlyList<EditorValueVariableFilter> Filters,
    string SelectedFilter,
    string Search,
    string Looking,
    string Details,
    bool IsEmpty,
    IReadOnlyList<EditorValueVariable> Shown);

internal static class EditorValueSnapshotFactory
{
    public static EditorValueSnapshot Build(ValuePickerViewModel picker)
    {
        var wall = picker.Wall;
        return new EditorValueSnapshot(
            picker.Mode.ToString(),
            picker.Title,
            picker.Hint,
            picker.Target,
            picker.TargetLine,
            picker.ShowsBand,
            picker.ShowsFormulaMaterials,
            picker.PanelSpan,
            picker.IsTextMode,
            picker.IsRunMode,
            picker.IsValueMode,
            picker.IsNameMode,
            Node(picker.Recipe),
            picker.Caret,
            picker.SpanEnd,
            picker.HeadlessCaret.Indices.ToArray(),
            picker.HeadlessCaretAfter,
            picker.SelectedPath?.Indices.ToArray(),
            picker.IsValueEmpty,
            picker.EmptyBandHint,
            picker.StructureHint,
            picker.Text,
            picker.Note,
            picker.NoteIsProblem,
            picker.Saying,
            picker.Reads,
            picker.HasSaying,
            picker.CanConfirm,
            picker.CanUndo,
            picker.CanRedo,
            picker.CanClear,
            picker.TextBody,
            picker.IsTypingNumber,
            picker.TypingText,
            picker.TypingPlaceholder,
            picker.CanInsertTyped,
            picker.TypingNote,
            picker.HasTypingNote,
            picker.FunctionQuery,
            picker.SelectedFunctionGroup?.Group,
            picker.IsAllFunctions,
            picker.HasChosenFunction,
            picker.HasChosenVariable,
            picker.AllowNewName,
            picker.NewName,
            picker.CanCreateName,
            picker.NewNameLine,
            [.. picker.Operators.Select(op => new EditorValueOperator(op.Token, op.Insert, op.Hint, op.Tone, op.IsEnabled))],
            [.. picker.Values.Select(value => new EditorValueToken(value.Text, value.Insert))],
            [.. picker.FunctionGroups.Select(group => new EditorValueFunctionGroup(group.Group, group.Label, group.IsSelected))],
            [.. picker.Functions.Select(function => new EditorValueFunction(
                function.Name, function.Tag, function.Heading, function.WrittenAs,
                function.ArityNote, function.Hint, function.Group, function.Insert,
                [.. function.Arguments.Select(argument => new EditorValueFunctionArgument(argument.Label, argument.Hint))]))],
            new EditorValueWall(
                [.. wall.Filters.Select(filter => new EditorValueVariableFilter(
                    filter.Group.ToString(), filter.Label, filter.Hint, filter.IsSelected))],
                wall.SelectedFilter?.Group.ToString() ?? "All",
                wall.SearchText,
                wall.Looking?.Name ?? string.Empty,
                wall.Details,
                wall.IsEmpty,
                [.. wall.Shown.Select(choice => new EditorValueVariable(
                    choice.Name, choice.Display, choice.Type?.ToString(), choice.Group.ToString(),
                    choice.Origin.ToString(), choice.Source, choice.Description,
                    choice.IsPersistent, choice.Tone, choice.Details))]));
    }

    private static EditorValueNode? Node(SlotNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var ghost = node switch
        {
            SlotCall call => call.Ghost,
            SlotList list => list.Ghost,
            _ => null,
        };
        var children = node switch
        {
            SlotRoot root => new List<SlotNode> { root.Child },
            SlotSequence sequence => [.. sequence.Items],
            SlotCall call => [.. call.Arguments],
            SlotBinary binary => new List<SlotNode> { binary.Left, binary.Right },
            SlotUnary unary => new List<SlotNode> { unary.Operand },
            SlotGroup group => new List<SlotNode> { group.Inner },
            SlotList list => [.. list.Items],
            _ => [],
        };

        return new EditorValueNode(
            Kind(node),
            node.Path?.Indices.ToArray(),
            node.Start,
            node.Length,
            node is SlotValue value ? value.Text : node is SlotEmpty empty ? empty.Text : string.Empty,
            node is SlotBinary binaryNode ? binaryNode.Operator
                : node is SlotUnary unaryNode ? unaryNode.Operator
                : string.Empty,
            node is SlotBinary or SlotUnary or SlotValue or SlotCall or SlotList
                ? (node switch
                {
                    SlotBinary binaryTone => binaryTone.Tone,
                    SlotUnary unaryTone => unaryTone.Tone,
                    SlotValue valueTone => valueTone.Tone,
                    SlotCall callTone => callTone.Tone,
                    SlotList listTone => listTone.Tone,
                    _ => "other",
                })
                : string.Empty,
            node is SlotCall callName ? callName.Name : string.Empty,
            node.Before,
            node is SlotValue { IsName: true },
            node is SlotEmpty { IsGhost: true },
            node is SlotEmpty { Required: true },
            node.IsEmpty,
            node.IsSelected,
            node.HasProblem,
            node.ProblemHint,
            node.HoverHint,
            node.CanRemove,
            node is SlotCall callArgs && callArgs.HasNoArguments,
            node is SlotList listItems && listItems.HasNoItems,
            node is SlotCall callStack && callStack.Stacked,
            node is SlotCall callCompact && callCompact.Compact,
            node is SlotSequence sequenceArg ? sequenceArg.ArgumentLabel : string.Empty,
            node is SlotSequence sequenceFlag ? sequenceFlag.ArgumentFlag : string.Empty,
            node is SlotSequence sequenceIndex ? sequenceIndex.ArgumentIndex : 0,
            node is SlotSequence sequenceOptional && sequenceOptional.ArgumentOptional,
            node is SlotSequence sequenceRequired && sequenceRequired.ArgumentRequired,
            [.. children.Select(child => Node(child)!)],
            Node(ghost));
    }

    private static string Kind(SlotNode node) => node switch
    {
        SlotRoot => "root",
        SlotSequence => "sequence",
        SlotCall => "call",
        SlotBinary => "binary",
        SlotUnary => "unary",
        SlotGroup => "group",
        SlotList => "list",
        SlotValue => "value",
        SlotEmpty => "empty",
        _ => "other",
    };
}
