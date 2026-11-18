using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// A condition read off the step's expression: either a comparison with two sides, or a group of
/// them joined by one way of joining. It is the neutral shape between the text the engine reads and
/// the lines the editor shows, so reading and writing stay in one place rather than in either view.
/// </summary>
public abstract record ConditionShape
{
    public sealed record Row(string Left, ConditionCompare Compare, string Right) : ConditionShape;

    public sealed record Group(Join Join, IReadOnlyList<ConditionShape> Children) : ConditionShape;
}

/// <summary>
/// How the lines of a condition are joined: every one of them, any one of them, or none of them.
/// "None" is not "not every": a condition that fails on all of its lines is a question worth asking
/// on its own, and writing it as <c>not (a or b)</c> says exactly that.
/// </summary>
public enum Join
{
    All,
    Any,
    None,
}

/// <summary>
/// Reads a condition out of the expression the step holds and writes it back. The language stays
/// the engine's own — <c>and</c>, <c>or</c>, <c>not</c>, the comparisons and the few functions that
/// are comparisons in disguise — so what the two views edit is exactly the text that was there
/// before, and nothing about the macro file changes.
/// </summary>
public static class Condition
{
    /// <summary>Every comparison a line may make, in the order the dropdown offers them.</summary>
    public static IReadOnlyList<ConditionCompareChoice> CompareChoices => Comparisons.All;

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
            return new ConditionShape.Group(binary.Operator == "or" ? Join.Any : Join.All, group);
        }

        // "None of these holds" is written as the denial of the one that says "any of these does",
        // so it is read back the same way round rather than as a row whose left side happens to be
        // a whole condition.
        if (tree is UnaryNode { Operator: "not" } denied
            && Denied(denied.Operand) is { } none)
        {
            return none;
        }

        return ReadRow(tree);
    }

    /// <summary>
    /// What a denial stands for when it wraps a group: only "any of these" has a counterpart that
    /// means something of its own, because denying "all of these" is a different question from
    /// "none of these" and is left as a row that says so.
    /// </summary>
    private static ConditionShape? Denied(ExpressionNode operand)
    {
        if (operand is not BinaryNode { Operator: "or" })
        {
            return null;
        }

        var group = new List<ConditionShape>();
        Gather((BinaryNode)operand, "or", group);
        return new ConditionShape.Group(Join.None, group);
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
        // A comparison written as a function — contains, startsWith, endsWith, match — is still one
        // row, and reads as one. Without this the whole call would land in the left-hand box and
        // the row would claim to be asking whether the call is true, which is not how it was meant.
        if (tree is CallNode call && Comparisons.All.FirstOrDefault(choice =>
                Comparisons.FunctionName(choice.Compare) is { } name
                && string.Equals(name, call.Name, System.StringComparison.OrdinalIgnoreCase)
                && choice.Shape is ComparisonShape.Call) is { } called
            && call.Arguments.Count == 2)
        {
            return new ConditionShape.Row(
                Expression.Render(call.Arguments[0]), called.Compare,
                Expression.Render(call.Arguments[1]));
        }

        if (tree is UnaryNode { Operator: "not" } unary)
        {
            // A denied call is the "does not contain" kind of row, not a row saying "false".
            if (unary.Operand is CallNode denied && denied.Arguments.Count == 2
                && Comparisons.All.FirstOrDefault(choice =>
                    Comparisons.FunctionName(choice.Compare) is { } name
                    && string.Equals(name, denied.Name, System.StringComparison.OrdinalIgnoreCase)
                    && choice.Shape is ComparisonShape.Call) is { } kept)
            {
                return new ConditionShape.Row(
                    Expression.Render(denied.Arguments[0]), Denial(kept.Compare),
                    Expression.Render(denied.Arguments[1]));
            }

            return new ConditionShape.Row(
                Expression.Render(unary.Operand), ConditionCompare.Fails, string.Empty);
        }

        if (tree is BinaryNode { } binary
            && Comparisons.All.FirstOrDefault(choice =>
                Comparisons.OperatorCode(choice.Compare) == binary.Operator) is { } comparison)
        {
            // length($a) == 0 and count($a) == 3 are rows of their own: written as a plain
            // comparison they would read as a left side nobody typed.
            if (binary.Left is CallNode wrapped && wrapped.Arguments.Count == 1)
            {
                var inside = Expression.Render(wrapped.Arguments[0]);
                if (string.Equals(wrapped.Name, "length", System.StringComparison.OrdinalIgnoreCase)
                    && IsZero(binary.Right))
                {
                    return new ConditionShape.Row(inside,
                        binary.Operator == "==" ? ConditionCompare.Empty : ConditionCompare.NotEmpty,
                        string.Empty);
                }

                if (string.Equals(wrapped.Name, "count", System.StringComparison.OrdinalIgnoreCase)
                    && binary.Operator == "==")
                {
                    return new ConditionShape.Row(inside, ConditionCompare.CountIs,
                        Expression.Render(binary.Right));
                }
            }

            return new ConditionShape.Row(
                Expression.Render(binary.Left), comparison.Compare, Expression.Render(binary.Right));
        }

        return new ConditionShape.Row(Expression.Render(tree), ConditionCompare.Holds, string.Empty);
    }

    /// <summary>The row that says a call did not hold, in the way that has words of its own.</summary>
    private static ConditionCompare Denial(ConditionCompare compare)
        => compare is ConditionCompare.Contains ? ConditionCompare.NotContains : compare;

    private static bool IsZero(ExpressionNode node)
        => node is NumberNode { Value: 0 };

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
        var operatorWord = group.Join == Join.All ? "and" : "or";
        foreach (var part in parts.Skip(1))
        {
            joined = new BinaryNode(operatorWord, joined, part);
        }

        return group.Join == Join.None ? new UnaryNode("not", joined) : joined;
    }

    private static ExpressionNode? BuildRow(ConditionShape.Row row)
    {
        if (Parse(row.Left) is not { } left)
        {
            // A half-filled line is left out rather than written as a broken condition: the step
            // then says it has no condition yet, which is the truth, instead of failing to run.
            return null;
        }

        var choice = Comparisons.Of(row.Compare);
        if (!choice.NeedsValue)
        {
            return choice.Shape switch
            {
                ComparisonShape.Bare => left,
                ComparisonShape.NegatedBare => new UnaryNode("not", left),
                ComparisonShape.NothingIn => new BinaryNode("==", LengthOf(left), Zero()),
                ComparisonShape.SomethingIn => new BinaryNode(">", LengthOf(left), Zero()),
                _ => null,
            };
        }

        if (Parse(row.Right) is not { } right)
        {
            return null;
        }

        if (choice.Shape is ComparisonShape.Operator)
        {
            return new BinaryNode(Comparisons.OperatorCode(row.Compare)!, left, right);
        }

        if (choice.Shape is ComparisonShape.HowMany)
        {
            return new BinaryNode("==", CountOf(left), right);
        }

        var call = new CallNode(
            Comparisons.FunctionName(row.Compare)!, [left, right]);
        return choice.Shape is ComparisonShape.NegatedCall ? new UnaryNode("not", call) : call;
    }

    private static CallNode LengthOf(ExpressionNode value) => new("length", [value]);

    private static CallNode CountOf(ExpressionNode value) => new("count", [value]);

    private static NumberNode Zero() => new("0", 0);

    private static ExpressionNode? Parse(string text)
        => Expression.TryParse(text, out var tree, out _) && tree is not null ? tree : null;
}
