using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// A condition read off the step's expression: either a comparison with two sides, or a group of
/// them joined by one operator. It is the neutral shape between the text the engine reads and the
/// lines the editor shows, so reading and writing stay in one place rather than in either view.
/// </summary>
public abstract record ConditionShape
{
    public sealed record Row(string Left, ConditionCompare Compare, string Right) : ConditionShape;

    public sealed record Group(bool Any, IReadOnlyList<ConditionShape> Children) : ConditionShape;
}

/// <summary>
/// Reads a condition out of the expression the step holds and writes it back. The language stays
/// the engine's own — <c>and</c>, <c>or</c>, <c>not</c> and the comparisons — so what the two views
/// edit is exactly the text that was there before, and nothing about the macro file changes.
/// </summary>
public static class Condition
{
    /// <summary>Every comparison a line may make, in the order the dropdown offers them.</summary>
    public static IReadOnlyList<ConditionCompareChoice> CompareChoices { get; } =
    [
        new(ConditionCompare.Equal, "==", "Condition.Is"),
        new(ConditionCompare.NotEqual, "!=", "Condition.IsNot"),
        new(ConditionCompare.Greater, ">", "Condition.Greater"),
        new(ConditionCompare.Less, "<", "Condition.Less"),
        new(ConditionCompare.AtLeast, ">=", "Condition.AtLeast"),
        new(ConditionCompare.AtMost, "<=", "Condition.AtMost"),
        new(ConditionCompare.Holds, string.Empty, "Condition.Holds"),
        new(ConditionCompare.Fails, string.Empty, "Condition.Fails"),
    ];

    /// <summary>
    /// Reads a condition. Text that is not a condition — a function call on its own, a shape the
    /// rows cannot say — comes back false rather than as a guess, and the editor keeps it as text.
    /// </summary>
    public static bool TryRead(string text, out ConditionShape shape)
    {
        shape = null!;
        if (!Expression.TryParse(text, out var tree, out _) || tree is null)
        {
            return false;
        }

        shape = Read(tree);
        return true;
    }

    /// <summary>The pieces a condition is made of, whichever way it is being shown.</summary>
    public static IReadOnlyList<ConditionShape> Children(ConditionShape shape)
        => shape is ConditionShape.Group group ? group.Children : [shape];

    /// <summary>Writes a condition as the expression the engine reads.</summary>
    public static string Render(ConditionShape shape)
        => Build(shape) is { } tree ? Expression.Render(tree) : string.Empty;

    private static ConditionShape Read(ExpressionNode tree)
    {
        if (tree is BinaryNode { Operator: "and" } or BinaryNode { Operator: "or" })
        {
            var binary = (BinaryNode)tree;
            var group = new List<ConditionShape>();
            Gather(binary, binary.Operator, group);
            return new ConditionShape.Group(binary.Operator == "or", group);
        }

        return ReadRow(tree);
    }

    /// <summary>Flattens <c>a and b and c</c> into one group rather than a stack of pairs.</summary>
    private static void Gather(ExpressionNode node, string operation, List<ConditionShape> into)
    {
        if (node is BinaryNode { } binary && binary.Operator == operation)
        {
            Gather(binary.Left, operation, into);
            Gather(binary.Right, operation, into);
            return;
        }

        into.Add(Read(node));
    }

    private static ConditionShape ReadRow(ExpressionNode tree)
    {
        if (tree is BinaryNode { } binary
            && CompareChoices.FirstOrDefault(choice =>
                choice.Code.Length > 0 && choice.Code == binary.Operator) is { } comparison)
        {
            return new ConditionShape.Row(
                Expression.Render(binary.Left), comparison.Compare, Expression.Render(binary.Right));
        }

        if (tree is UnaryNode { Operator: "not" } unary)
        {
            return new ConditionShape.Row(
                Expression.Render(unary.Operand), ConditionCompare.Fails, string.Empty);
        }

        return new ConditionShape.Row(Expression.Render(tree), ConditionCompare.Holds, string.Empty);
    }

    private static ExpressionNode? Build(ConditionShape shape)
        => shape switch
        {
            ConditionShape.Group group => BuildGroup(group),
            ConditionShape.Row row => BuildRow(row),
            _ => null,
        };

    private static ExpressionNode? BuildGroup(ConditionShape.Group group)
    {
        var parts = group.Children
            .Select(Build)
            .OfType<ExpressionNode>()
            .ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        var joined = parts[0];
        foreach (var part in parts.Skip(1))
        {
            joined = new BinaryNode(group.Any ? "or" : "and", joined, part);
        }

        return joined;
    }

    private static ExpressionNode? BuildRow(ConditionShape.Row row)
    {
        if (Parse(row.Left) is not { } left)
        {
            // A half-filled line is left out rather than written as a broken condition: the step
            // then says it has no condition yet, which is the truth, instead of failing to run.
            return null;
        }

        if (row.Compare is ConditionCompare.Holds)
        {
            return left;
        }

        if (row.Compare is ConditionCompare.Fails)
        {
            return new UnaryNode("not", left);
        }

        var choice = CompareChoices.First(candidate => candidate.Compare == row.Compare);
        return Parse(row.Right) is { } right ? new BinaryNode(choice.Code, left, right) : null;
    }

    private static ExpressionNode? Parse(string text)
        => Expression.TryParse(text, out var tree, out _) && tree is not null ? tree : null;
}
