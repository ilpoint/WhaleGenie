using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Turns the little condition descriptions a test writes into the tree a condition field holds.
/// These <c>condition.*</c> steps are a fixture's shorthand, not actions the engine runs: a test
/// says "compare n greater than 3" and gets the same shape the editor would have built, without a
/// line of formula text being written and read back in between.
/// </summary>
internal static class TestConditions
{
    public static ExpressionNode Expression(ExecutableStep condition) => condition.Type switch
    {
        "condition.expression" => condition.Written("expression"),
        "condition.compare" => Compare(condition),
        "condition.stepResult" =>
            Formula.Binary("==", Formula.Name("step." + Plain(condition, "stepId") + ".outcome"),
                Formula.Text(Plain(condition, "expectedOutcome"))),
        "condition.listContains" => Formula.Call("contains",
            Name(condition, "list"), Formula.Text(Plain(condition, "wantedValue"))),
        "condition.valueInRange" => Formula.Binary("and",
            Formula.Binary(">=", Name(condition, "testValue"), Number(condition, "min")),
            Formula.Binary("<=", Name(condition, "testValue"), Number(condition, "max"))),
        "condition.randomChance" => Formula.Binary("<=",
            Formula.Call("random", Formula.Number(1), Formula.Number(100)),
            Number(condition, "chancePercent")),
        "condition.dateCompare" => Plain(condition, "dateOperator") switch
        {
            "before" => Formula.Binary("<", Name(condition, "left"), Name(condition, "right")),
            "equals" => Formula.Binary("==", Name(condition, "left"), Name(condition, "right")),
            "sameDay" => Formula.Binary("==",
                Formula.Call("formatDate", Name(condition, "left"), Formula.Text("yyyy-MM-dd")),
                Formula.Call("formatDate", Name(condition, "right"), Formula.Text("yyyy-MM-dd"))),
            _ => Formula.Binary(">", Name(condition, "left"), Name(condition, "right")),
        },
        "condition.group" => Group(condition),
        _ => Formula.Constant("false"),
    };

    private static ExpressionNode Compare(ExecutableStep condition)
    {
        var left = Name(condition, "variable");
        var right = Plain(condition, "compareValue");
        var wanted = decimal.TryParse(right, out var number)
            ? Formula.Number(right, (double)number)
            : Formula.Text(right);

        return Plain(condition, "compareOperator") switch
        {
            "notEquals" => Formula.Binary("!=", left, wanted),
            "greaterThan" => Formula.Binary(">", left, wanted),
            "greaterOrEqual" => Formula.Binary(">=", left, wanted),
            "lessThan" => Formula.Binary("<", left, wanted),
            "lessOrEqual" => Formula.Binary("<=", left, wanted),
            "contains" => Formula.Call("contains", left, Formula.Text(right)),
            "notContains" => Formula.Unary("not",
                Formula.Call("contains", left, Formula.Text(right))),
            "exists" => Formula.Binary("and",
                Formula.Binary("!=", left, Formula.Constant("null")),
                Formula.Binary("!=", left, Formula.Text(string.Empty))),
            "regex" => Formula.Call("match", left, Formula.Text(right)),
            _ => Formula.Binary("==", left, wanted),
        };
    }

    private static ExpressionNode Group(ExecutableStep condition)
    {
        var joined = condition.Children("conditions").Select(Expression).ToList();
        if (joined.Count == 0)
        {
            return Formula.Constant("true");
        }

        var op = Plain(condition, "op");
        var combined = joined.Aggregate((left, right) =>
            Formula.Binary(op is "or" or "none" ? "or" : "and", left, right));

        return op == "none" ? Formula.Unary("not", combined) : combined;
    }

    private static string Plain(ExecutableStep condition, string name)
        => Formula.Plain(condition.Written(name));

    private static ExpressionNode Name(ExecutableStep condition, string name)
        => Formula.Name(Plain(condition, name).TrimStart('$'));

    private static ExpressionNode Number(ExecutableStep condition, string name)
    {
        var text = Plain(condition, name).Trim();
        return double.TryParse(text, out var number)
            ? Formula.Number(text, number)
            : Formula.Number(0);
    }
}
