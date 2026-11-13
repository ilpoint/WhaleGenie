using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Calls into the macro itself, and the fields a declared record does and does not have.
public partial class MacroCheckTests
{
    [Fact]
    public void A_call_to_the_macro_itself_inside_a_branch_or_loop_is_left_alone()
    {
        var branch = Step("control.if",
            When("condition", "true"),
            Steps("then", Call("自己")));
        var switching = Step("control.switch",
            Text("switchValue", "1"), Text("switchMode", "equals"),
            Steps("cases", Step("control.case", Text("values", "1"),
                Steps("body", Call("自己")))),
            Steps("otherwise"));

        Assert.Empty(Against("自己", [], branch));
        Assert.Empty(Against("自己", [], switching));
        Assert.Empty(Against("自己", [], Repeat(Call("自己"))));
    }

    /// <summary>
    /// A record whose declaration names its fields is checkable while the macro is written; one that
    /// names none is not, because checking against names nobody wrote down would be a guess.
    /// </summary>
    private static MacroCallable WithRecord(string name,
        params (string Name, ValueKind Type)[] fields)
        => new(name,
            [
                new MacroParameter
                {
                    Name = "点",
                    Type = ValueKind.Record,
                    Required = true,
                    Fields = [.. fields.Select(field => new MacroField
                    {
                        Name = field.Name,
                        Type = field.Type,
                    })],
                },
            ], []);

    [Fact]
    public void A_field_a_declared_record_has_is_left_alone()
        => Assert.Empty(Ui.Run(() => MacroCheck.Inspect(
            [Set("左", "field($点, \"x\")")],
            WithRecord("自己", ("x", ValueKind.Number), ("y", ValueKind.Number)),
            [])));

    [Fact]
    public void A_field_a_declared_record_does_not_have_is_marked()
    {
        var step = Set("左", "field($点, \"z\")");

        var problem = Assert.Single(Ui.Run(() => MacroCheck.Inspect(
            [step],
            WithRecord("自己", ("x", ValueKind.Number), ("y", ValueKind.Number)),
            [])));

        // The mark names the field that is not there and the ones that are: "wrong field" without
        // the list would leave the author guessing at the spelling.
        Assert.Equal("UnknownField", problem.Code);
        Assert.Same(step, problem.Step);
        Assert.Contains("z", problem.Message, StringComparison.Ordinal);
        Assert.Contains("x, y", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_that_names_no_fields_is_not_checked()
        => Assert.Empty(Ui.Run(() => MacroCheck.Inspect(
            [Set("左", "field($点, \"随便\")")],
            WithRecord("自己"),
            [])));

    [Fact]
    public void A_record_a_call_hands_back_is_checked_by_the_called_macro()
    {
        var called = new MacroCallable("找图",
            [new MacroParameter { Name = "图", Required = true }],
            [
                new MacroResult
                {
                    Name = "命中",
                    Type = ValueKind.Record,
                    Fields =
                    [
                        new MacroField { Name = "left", Type = ValueKind.Number },
                        new MacroField { Name = "top", Type = ValueKind.Number },
                    ],
                },
            ]);

        var call = Step("control.runMacro", Text("macro", "找图"), Arguments(("图", Formula.Text("a.png"))),
            Results("命中"));

        // Reading a field of a record a call handed back is checked against the called macro's
        // declaration: nowhere else says what is in it.
        Assert.Empty(Ui.Run(() => MacroCheck.Inspect(
            [call, Set("左", "field($命中, \"left\")")], Callable("自己"), [called])));

        var problem = Assert.Single(Ui.Run(() => MacroCheck.Inspect(
            [call, Set("左", "field($命中, \"middle\")")], Callable("自己"), [called])));
        Assert.Equal("UnknownField", problem.Code);
    }


    [Fact]
    public void Two_calls_side_by_side_writing_one_name_are_marked()
    {
        var steps = new[]
        {
            Step("control.runMacro", Text("macro", "甲"), Results("一")),
            Step("control.runMacro", Text("macro", "甲"), Results("一")),
        };

        var problem = Assert.Single(Against("自己", [Callable("甲", results: ["一"])], steps),
            candidate => candidate.Code == "DuplicateResultName");
        Assert.Same(steps[1], problem.Step);
    }
}
