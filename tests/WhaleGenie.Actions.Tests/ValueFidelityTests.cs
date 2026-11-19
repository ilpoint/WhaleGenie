using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// A field is a shape, not a line. Opening it or filling it from the window must keep the identity
/// each name carries, or a reference picked once would lose its binding the next time the step was
/// opened and saved.
/// </summary>
public class ValueFidelityTests
{
    private static ActionParameter Definition(string action, string parameter)
        => ActionCatalog.Find(action)!.Parameters.First(one => one.Name == parameter);

    [Fact]
    public void Opening_a_formula_keeps_each_id()
    {
        var definition = Definition("system.notify", "notificationText");
        var field = new StepParameterViewModel(definition);
        var written = Formula.Run([Formula.Text("你好，"), Formula.Name("count")]);

        field.ApplyValue(new StepParameter
        {
            Name = "notificationText",
            Kind = definition.Kind,
            Written = written,
        });

        Assert.Equal(written, field.ToStepParameter().Written);
    }

    [Fact]
    public void Writing_a_formula_keeps_each_id()
    {
        var definition = Definition("system.notify", "notificationText");
        var field = new StepParameterViewModel(definition);
        var written = Formula.Name("count");

        field.WriteValue(written);

        Assert.Equal(written, field.ToStepParameter().Written);
    }

    [Fact]
    public void A_name_field_shows_the_name_without_the_sign()
    {
        var definition = Definition("control.setVariable", "name");
        var field = new StepParameterViewModel(definition);

        field.ApplyValue(new StepParameter
        {
            Name = "name",
            Kind = definition.Kind,
            Written = Formula.Name("count"),
        });

        Assert.Equal("count", field.Text);
    }

    [Fact]
    public void A_switch_answers_with_a_constant_not_with_a_line()
    {
        var definition = Definition("input.mouseClick", "useCursor");
        var field = new StepParameterViewModel(definition);

        // The default is already the shape it answers with: no change had to happen to put it in.
        Assert.Equal(Formula.Constant("false"), field.ToStepParameter().Written);

        field.Flag = true;

        Assert.Equal(Formula.Constant("true"), field.ToStepParameter().Written);
    }

    [Fact]
    public void A_chosen_option_answers_with_the_word_it_chose()
    {
        var definition = Definition("control.setVariable", "scope");
        var field = new StepParameterViewModel(definition);

        field.Option = field.Choices.Single(choice => choice.Value == "global");

        Assert.Equal(Formula.Text("global"), field.ToStepParameter().Written);
    }

    [Fact]
    public void Opening_a_switch_keeps_the_shape_the_file_held()
    {
        var definition = Definition("input.mouseClick", "useCursor");
        var field = new StepParameterViewModel(definition);

        field.ApplyValue(new StepParameter
        {
            Name = "useCursor",
            Kind = definition.Kind,
            Written = Formula.Constant("true"),
        });

        Assert.True(field.Flag);
        Assert.Equal(Formula.Constant("true"), field.ToStepParameter().Written);
    }

    [Fact]
    public void Clearing_a_line_does_not_take_a_switch_s_answer_away()
    {
        var definition = Definition("input.mouseClick", "useCursor");
        var field = new StepParameterViewModel(definition);
        field.Flag = true;

        field.Clear();

        Assert.True(field.Flag);
        Assert.Equal(Formula.Constant("true"), field.ToStepParameter().Written);
    }

    [Fact]
    public void A_name_the_window_hands_over_is_drawn_without_a_sign()
    {
        var definition = Definition("system.notify", "notificationText");
        var field = new StepParameterViewModel(definition);
        var written = Formula.Run([Formula.Text("你好，"), Formula.Name("count")]);

        field.WriteValue(written);

        Assert.DoesNotContain("$", field.Text);
        Assert.Contains("「count」", field.Text);
        Assert.Equal(written, field.ToStepParameter().Written);
    }

    [Fact]
    public void Names_are_offered_as_bare_names_not_as_spoken_pieces()
    {
        var definition = Definition("system.notify", "notificationText");
        var field = new StepParameterViewModel(definition,
        [
            new VariableChoice { Name = "count", Scope = VariableScope.Local },
        ]);

        Assert.Contains("count", field.ExpressionSuggestions);
        Assert.DoesNotContain("「count」", field.ExpressionSuggestions);
    }
}
