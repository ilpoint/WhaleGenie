using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Tests;

// What the action catalogue says: unknown actions and fields, repeated results, and required fields.
public partial class MacroCheckTests
{
    [Fact]
    public void Assigning_a_missing_global_is_reported_before_the_run()
    {
        var name = "missingGlobalForCheck";
        var step = Step("control.setVariable", Text("name", name), Text("scope", "global"),
            Value("value", "1"));

        Assert.Contains(Inspect(step),
            problem => problem.Message == Strings.Format("Editor.Problem.GlobalMustExist", name));

        var definition = new VariableDefinition
        {
            Name = name,
            Scope = VariableScope.Global,
        };
        VariableCatalog.Globals.Add(definition);
        try
        {
            Assert.DoesNotContain(Inspect(step),
                problem => problem.Message == Strings.Format("Editor.Problem.GlobalMustExist", name));
        }
        finally
        {
            VariableCatalog.Globals.Remove(definition);
        }
    }

    [Fact]
    public void One_step_can_be_wrong_more_than_once_and_each_reason_is_said()
    {
        var step = Set("结果", "甲 + 乙");

        var problems = Inspect(step);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Same(step, problem.Step));
    }

    [Fact]
    public void An_action_the_catalogue_does_not_know_is_marked()
    {
        var problem = Assert.Single(Inspect(Step("old.gone", Text("anything", "1"))));

        Assert.Equal(Strings.Get("Editor.Problem.UnknownAction"), problem.Message);
    }

    [Fact]
    public void A_field_the_catalogue_does_not_know_is_marked()
    {
        var problem = Assert.Single(Inspect(Step("control.delay",
            Text("ms", "1000"), Text("wait", "true"))));

        Assert.Equal(Strings.Format("Editor.Problem.UnknownParameter", "wait"), problem.Message);
    }

    [Fact]
    public void A_place_written_twice_is_marked()
    {
        var step = Step("window.move", Text("rect", "1,2,3,4"), Text("width", "800"));

        var problem = Assert.Single(Inspect(step));
        var label = ActionCatalog.Find("window.move")!.Parameters.First(parameter => parameter.Name == "rect")
            .LocalLabel;

        Assert.Equal(Strings.Format("Editor.Problem.PlaceAndNumbers", label), problem.Message);
    }

    [Fact]
    public void Two_results_with_one_name_are_marked()
    {
        var step = Step("file.readText", Text("path", "a.txt"),
            Returned(("text", "part"), ("lines", "part")));

        var problem = Assert.Single(Inspect(step));

        Assert.Equal(Strings.Format("Editor.Problem.DuplicateResultName", "part"), problem.Message);
    }

    [Fact]
    public void A_result_the_action_does_not_declare_is_marked()
    {
        var step = Step("file.path", Text("path", "a.txt"), Results("missing"));

        var problem = Assert.Single(Inspect(step));
        var action = ActionCatalog.Find("file.path")!.LocalName;

        Assert.Equal(Strings.Format("Editor.Problem.UndeclaredActionResult", "missing", action),
            problem.Message);
    }

    [Fact]
    public void One_result_key_asked_for_twice_is_marked()
    {
        var step = Step("file.readText", Text("path", "a.txt"),
            Returned(("text", "first"), ("text", "second")));

        var problem = Assert.Single(Inspect(step));

        Assert.Equal(Strings.Format("Editor.Problem.DuplicateResultKey", "text"), problem.Message);
    }

    [Fact]
    public void A_result_name_the_macro_language_cannot_read_is_marked()
    {
        var step = Step("file.path", Text("path", "a.txt"),
            Returned(("path", "two words")));

        var problem = Assert.Single(Inspect(step));

        Assert.Equal(Strings.Format("Editor.Problem.BadResultName", "two words"),
            problem.Message);
    }
    [Fact]
    public void A_required_field_left_empty_is_marked()
    {
        var problem = Assert.Single(Inspect(Step("browser.goTo")));
        var label = ActionCatalog.Find("browser.goTo")!.Parameters.First(parameter => parameter.Name == "url")
            .LocalLabel;

        Assert.Equal(Strings.Format("Editor.Problem.MissingField", label), problem.Message);
    }

    [Fact]
    public void A_choice_outside_the_catalogue_is_marked()
    {
        var problems = Inspect(Step("file.writeText",
            Text("path", "out.txt"), Text("writeMode", "sideways")));

        Assert.Contains(problems, problem =>
            problem.Message.Contains("sideways", StringComparison.Ordinal));
    }
}
