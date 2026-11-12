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
        var written = Formula.Run([Formula.Text("你好，"), Formula.Name("count", "a7k2m9")]);

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
        var written = Formula.Name("count", "a7k2m9");

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
            Written = Formula.Name("count", "a7k2m9"),
        });

        Assert.Equal("count", field.Text);
    }
}
