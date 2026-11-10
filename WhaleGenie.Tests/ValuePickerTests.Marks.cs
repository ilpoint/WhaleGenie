using System.Collections.Generic;
using System.Linq;
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
        var form = new ValuePickerViewModel("substring($a, 1)", Variables(), namesOnly: false);
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
        var form = new ValuePickerViewModel("substring($a)", Variables(), namesOnly: false);
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
        var form = new ValuePickerViewModel("nope($a)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(call.HasProblem);
        Assert.NotEmpty(call.ProblemHint);
        Assert.False(Root(form).HasProblem);
    }

    /// <summary>Writing a readable formula clears every mark and the reason the button was grey.</summary>
    [Fact]
    public void A_good_formula_clears_the_marks_and_the_reason()
    {
        var form = new ValuePickerViewModel("nope($a)", Variables(), namesOnly: false);
        Assert.Contains(form.Recipe!.Walk(), one => one.HasProblem);

        form.Text = "upper($a)";

        Assert.DoesNotContain(form.Recipe!.Walk(), one => one.HasProblem);
        Assert.True(form.CanConfirm);
        Assert.False(form.HasBlockReason);
    }
}
