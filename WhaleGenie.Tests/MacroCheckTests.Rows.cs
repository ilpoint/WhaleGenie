using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// Declarations and rows: an unknown column, a broken expression, and the colour search fields.
public partial class MacroCheckTests
{

    [Fact]
    public void A_required_field_that_only_applies_under_some_settings_is_left_alone()
        => Assert.Empty(Inspect(Step("data.hash",
            Text("hashSource", "text"), Text("dataText", "abc"))));

    [Fact]
    public void A_call_to_a_macro_with_a_bad_declaration_is_marked()
    {
        var bad = Callable("坏", ["同名", "同名"], [], []);
        var call = Step("control.runMacro", Text("macro", "坏"));

        var problems = Against("调用方", [bad], call);

        var problem = Assert.Single(problems, candidate => candidate.Step == call);
        Assert.Equal(Strings.Format("Editor.Problem.BadDeclaration", "坏", "同名"),
            problem.Message);
    }

    [Fact]
    public void A_row_with_an_unknown_column_is_marked()
    {
        var returns = new StepParameter
        {
            Name = "returns",
            Kind = ActionParameterKind.Returns,
            Rows =
            [
                new StepParameterRow
                {
                    Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = Formula.Text("path"),
            ["name"] = Formula.Text("part"),
            ["bogus"] = Formula.Text("x"),
        },
                },
            ],
        };

        var problems = Inspect(Step("file.path", Text("path", "a.txt"),
            Text("operation", "name"), returns));

        Assert.Contains(problems, problem =>
            problem.Message.Contains("bogus", StringComparison.Ordinal));
    }

    [Fact]
    public void Declaration_problems_are_said_without_a_call()
    {
        var bad = Callable("坏", ["同名", "同名"], [""], []);

        var problems = MacroCheck.DeclarationProblems(bad);

        Assert.Contains(problems, message => message.Contains("同名", StringComparison.Ordinal));
        Assert.Contains(problems, message =>
            message == Strings.Format("Editor.Problem.EmptyDeclaration", "坏"));
    }

    [Fact]
    public void A_broken_braced_expression_in_text_is_marked()
    {
        var problems = Inspect(Step("control.log", Text("message", "${1 +}"),
            Text("level", "info")));

        var prefix = Strings.Get("Editor.Problem.BadExpression").Split("{0}")[0];
        Assert.Contains(problems, problem =>
            problem.Message.StartsWith(prefix, StringComparison.Ordinal));
    }

    [Fact]
    public void A_colour_search_can_name_only_its_region()
    {
        var region = new StepParameter
        {
            Name = "region",
            Kind = ActionParameterKind.Region,
            Rows =
            [
                new StepParameterRow
                {
                    Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["x"] = Formula.Text("0"),
            ["y"] = Formula.Text("0"),
            ["width"] = Formula.Text("20"),
            ["height"] = Formula.Text("20"),
        },
                },
            ],
        };

        var problems = Inspect(Step("vision.findColor", Text("color", "#FF0000"), region));

        Assert.DoesNotContain(problems, problem =>
            problem.Message.StartsWith(Strings.Get("Editor.Problem.NoPlace").Split("{0}")[0],
                StringComparison.Ordinal));
    }

    [Fact]
    public void A_colour_search_place_and_its_numbers_are_marked()
    {
        var step = Step("vision.getPixel",
            Text("place", "$spot"), Text("x", "10"), Text("y", "20"));

        var label = ActionCatalog.Find("vision.getPixel")!.Parameters
            .First(parameter => parameter.Name == "place").LocalLabel;
        Assert.Contains(Inspect(step), problem =>
            problem.Message == Strings.Format("Editor.Problem.PlaceAndNumbers", label));
    }

    [Fact]
    public void A_region_row_cannot_name_a_place_and_give_its_numbers()
    {
        var region = new StepParameter
        {
            Name = "region",
            Kind = ActionParameterKind.Region,
            Rows =
            [
                new StepParameterRow
                {
                    Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = Formula.Text("game"),
            ["x"] = Formula.Text("0"),
            ["y"] = Formula.Text("0"),
        },
                },
            ],
        };

        var problems = Inspect(Step("vision.findImage", region));
        var label = ActionCatalog.Find("vision.findImage")!.Parameters
            .First(parameter => parameter.Name == "region").LocalLabel;

        Assert.Contains(problems, problem =>
            problem.Message == Strings.Format("Editor.Problem.ExclusiveRowColumn", label));
    }
}
