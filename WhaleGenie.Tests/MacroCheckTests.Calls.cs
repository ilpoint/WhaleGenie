using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Calling another macro: its arguments, their kinds, and the results it hands back.
public partial class MacroCheckTests
{
    [Fact]
    public void A_call_that_names_no_macro_is_reported()
        => Assert.Single(Against("自己", [Callable("另一个")], Call("")));

    [Fact]
    public void A_call_that_fills_in_a_name_the_called_macro_never_declared_is_reported()
    {
        var passed = Step("control.runMacro",
            Text("macro", "另一个"), Arguments(("别的", "1")));

        var problems = Against("自己", [Callable("另一个", parameters: ["次数"])], passed);
        var problem = Assert.Single(problems, candidate =>
            candidate.Message.Contains("别的", StringComparison.Ordinal));

        Assert.Same(passed, problem.Step);

        // The call also leaves the required parameter out, and that is said on its own.
        Assert.Contains(problems, candidate =>
            candidate.Step == passed
            && candidate.Message == Strings.Format("Editor.Problem.MissingArgument", "次数", "另一个"));
    }

    [Fact]
    public void A_call_that_leaves_a_required_argument_out_is_marked()
    {
        var call = Step("control.runMacro", Text("macro", "另一个"));

        var problems = Against("自己", [Callable("另一个", parameters: ["次数"])], call);

        Assert.Contains(problems, problem =>
            problem.Step == call
            && problem.Message == Strings.Format("Editor.Problem.MissingArgument", "次数", "另一个"));
    }

    [Fact]
    public void A_given_marker_for_an_undeclared_argument_is_marked()
    {
        var call = Step("control.runMacro",
            Text("macro", "另一个"), ArgumentsGiven(("别的", "")));

        var problems = Against("自己", [Callable("另一个", parameters: ["次数"])], call);

        Assert.Contains(problems, problem =>
            problem.Message == Strings.Format("Editor.Problem.UndeclaredArgument", "别的", "另一个"));
    }

    [Fact]
    public void A_given_marker_beside_a_value_is_marked()
    {
        var call = Step("control.runMacro",
            Text("macro", "另一个"), ArgumentsGiven(("次数", "1")));

        var problems = Against("自己", [Callable("另一个", parameters: ["次数"])], call);

        Assert.Contains(problems, problem =>
            problem.Message == Strings.Format("Editor.Problem.GivenWithValue", "次数"));
    }

    [Fact]
    public void A_constant_argument_of_the_wrong_kind_is_marked()
    {
        var called = new MacroCallable("另一个",
            [new MacroParameter
            {
                Name = "数量",
                Required = true,
                Type = ValueKind.Number,
            }], []);
        var call = Step("control.runMacro",
            Text("macro", "另一个"), Arguments(("数量", "不是数")));

        var problems = Against("自己", [called], call);

        Assert.Contains(problems, problem =>
            problem.Step == call
            && problem.Message == Strings.Format("Editor.Problem.ArgumentType", "数量",
                Strings.Get("ActionType.Number")));
    }

    [Fact]
    public void A_default_of_the_wrong_kind_is_reminded()
    {
        var called = new MacroCallable("另一个",
            [new MacroParameter
            {
                Name = "数量",
                Required = false,
                Default = Formula.Text("不是数"),
                Type = ValueKind.Number,
            }], []);

        var messages = MacroCheck.DeclarationProblems(called);

        Assert.Contains(messages, message =>
            message == Strings.Format("Editor.Problem.BadDefaultType", "另一个", "数量",
                Strings.Get("ActionType.Number")));
    }

    [Fact]
    public void A_default_with_a_broken_expression_is_reminded()
    {
        var called = new MacroCallable("另一个",
            [new MacroParameter
            {
                Name = "数量",
                Required = false,
                Default = Formula.Binary("+", Formula.Hole, Formula.Number(1)),
                Type = ValueKind.Number,
            }], []);

        var messages = MacroCheck.DeclarationProblems(called);
        var prefix = Strings.Get("Editor.Problem.BadExpression").Split("{0}")[0];

        Assert.Contains(messages, message => message.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void A_call_that_asks_for_a_result_the_called_macro_never_offered_is_reported()
    {
        var asked = Call("另一个", "别的");

        var problem = Assert.Single(Against("自己",
            [Callable("另一个", results: ["答案"])], asked));

        Assert.Same(asked, problem.Step);
    }

    [Fact]
    public void A_macro_may_read_the_results_it_declares()
    {
        var reading = Set("副本", "$答案");
        var own = new MacroCallable("自己", [], [new MacroResult { Name = "答案" }], [reading]);

        Assert.Empty(Ui.Run(() => MacroCheck.Inspect([reading], own, [own])));
    }
}
