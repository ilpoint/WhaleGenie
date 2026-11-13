using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// What a call says about its arguments, and the marks a wrong formula carries.
public partial class ValuePickerTests
{
    /// <summary>Written arguments say what they are for, not just the empty ones.</summary>
    [Fact]
    public void Arguments_carry_a_name_a_position_and_whether_they_are_optional()
    {
        var form = new ValuePickerViewModel("substring($a, 1)", Variables(), ActionValueForm.Value, bareNames: true);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(1, call.Arguments[0].ArgumentIndex);
        Assert.Equal(2, call.Arguments[1].ArgumentIndex);
        Assert.True(call.Arguments[0].HasArgumentLabel);
        Assert.True(call.Arguments[0].HasArgumentFlag);
        Assert.False(call.Arguments[0].ArgumentOptional);
    }

    /// <summary>A missing required argument is pointed at the place that is still empty.</summary>
    [Fact]
    public void A_missing_required_argument_is_marked_on_the_place_it_belongs_to()
    {
        var form = new ValuePickerViewModel("substring($a)", Variables(), ActionValueForm.Value, bareNames: true);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        var ghost = Assert.IsType<SlotEmpty>(call.Ghost);

        Assert.True(ghost.HasProblem);
        Assert.NotEmpty(ghost.ProblemHint);
        Assert.Equal(ghost.ProblemHint, ghost.HoverHint);
        Assert.False(form.CanConfirm);
        Assert.True(form.HasBlockReason);
    }

    /// <summary>An unknown function is marked on its call, not on the whole formula.</summary>
    [Fact]
    public void An_unknown_function_is_marked_on_its_call()
    {
        var form = new ValuePickerViewModel("nope($a)", Variables(), ActionValueForm.Value, bareNames: true);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(call.HasProblem);
        Assert.NotEmpty(call.ProblemHint);
        Assert.False(Root(form).HasProblem);
    }

    /// <summary>Writing a readable formula clears every mark and the reason the button was grey.</summary>
    [Fact]
    public void A_good_formula_clears_the_marks_and_the_reason()
    {
        var form = new ValuePickerViewModel("nope($a)", Variables(), ActionValueForm.Value, bareNames: true);
        Assert.Contains(form.Recipe!.Walk(), one => one.HasProblem);

        form.Text = "upper($count)";

        Assert.DoesNotContain(form.Recipe!.Walk(), one => one.HasProblem);
        Assert.True(form.CanConfirm);
        Assert.False(form.HasBlockReason);
    }

    /// <summary>A name nothing declares is pointed at, rather than discovered when the macro runs.</summary>
    [Theory]
    [InlineData("$a + 1")]
    [InlineData("missing + 1")]
    public void A_name_no_variable_declares_is_marked(string formula)
    {
        var form = new ValuePickerViewModel(formula, Variables(), ActionValueForm.Value,
            bareNames: true);
        var name = Root(form).Items.OfType<SlotValue>().First(one => one.IsName);

        Assert.True(name.HasProblem);
        Assert.NotEmpty(name.ProblemHint);
    }

    /// <summary>A name the wall offers is not a mistake.</summary>
    [Fact]
    public void A_declared_name_is_not_marked()
    {
        var form = new ValuePickerViewModel("$count + 1", Variables(), ActionValueForm.Value,
            bareNames: true);

        Assert.DoesNotContain(form.Recipe!.Walk(), one => one.HasProblem);
    }

    /// <summary>A bare word in a field that holds text is a word, not a name to look up.</summary>
    [Fact]
    public void A_word_in_a_text_field_is_not_a_name()
    {
        var form = new ValuePickerViewModel("missing", Variables(), ActionValueForm.Value);

        Assert.DoesNotContain(form.Recipe!.Walk(), one => one.HasProblem);
    }
}
