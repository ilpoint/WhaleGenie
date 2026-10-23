using System;
using System.Linq;
using System.Text.Json;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>Turns the old inline condition builders into the Boolean expressions the engine now reads.</summary>
internal static class TestConditions
{
    public static string Expression(ExecutableStep condition) => condition.Type switch
    {
        "condition.expression" => condition.Text("expression"),
        "condition.compare" => Compare(condition),
        "condition.stepResult" =>
            $"$step.{condition.Text("stepId")}.outcome == {Quote(condition.Text("expectedOutcome"))}",
        "condition.listContains" =>
            $"contains({condition.Text("list")}, {Quote(condition.Text("wantedValue"))})",
        "condition.valueInRange" =>
            $"{condition.Text("testValue")} >= {Number(condition.Text("min"))} and "
            + $"{condition.Text("testValue")} <= {Number(condition.Text("max"))}",
        "condition.randomChance" =>
            $"random(1, 100) <= {Number(condition.Text("chancePercent"))}",
        "condition.dateCompare" => condition.Text("dateOperator") switch
        {
            "before" => $"{condition.Text("left")} < {condition.Text("right")}",
            "equals" => $"{condition.Text("left")} == {condition.Text("right")}",
            "sameDay" => $"formatDate({condition.Text("left")}, \"yyyy-MM-dd\") == "
                + $"formatDate({condition.Text("right")}, \"yyyy-MM-dd\")",
            _ => $"{condition.Text("left")} > {condition.Text("right")}",
        },
        "condition.group" => Group(condition),
        _ => "false",
    };

    private static string Compare(ExecutableStep condition)
    {
        var left = condition.Text("variable");
        if (!left.StartsWith('$'))
        {
            left = "$" + left;
        }

        var right = condition.Text("compareValue");
        var wanted = decimal.TryParse(right, out _) ? right : Quote(right);
        return condition.Text("compareOperator") switch
        {
            "notEquals" => $"{left} != {wanted}",
            "greaterThan" => $"{left} > {wanted}",
            "greaterOrEqual" => $"{left} >= {wanted}",
            "lessThan" => $"{left} < {wanted}",
            "lessOrEqual" => $"{left} <= {wanted}",
            "contains" => $"contains({left}, {Quote(right)})",
            "notContains" => $"not contains({left}, {Quote(right)})",
            "exists" => $"{left} != null and {left} != \"\"",
            "regex" => $"match({left}, {Quote(right)})",
            _ => $"{left} == {wanted}",
        };
    }

    private static string Group(ExecutableStep condition)
    {
        var joined = condition.Children("conditions")
            .Select(child => $"({Expression(child)})")
            .ToList();
        if (joined.Count == 0)
        {
            return "true";
        }

        var separator = condition.Text("op") switch
        {
            "or" => " or ",
            "none" => " or ",
            _ => " and ",
        };
        var text = string.Join(separator, joined);
        return condition.Text("op") == "none" ? "not (" + text + ")" : text;
    }

    private static string Number(string text) => text.Trim().Length == 0 ? "0" : text.Trim();

    private static string Quote(string text) => JsonSerializer.Serialize(text);
}
