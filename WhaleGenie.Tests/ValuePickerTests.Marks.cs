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
        var form = new ValuePickerViewModel(ValuePickerTests.Seed("substring(a, 1)"), Variables(), ActionValueForm.Value);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(1, call.Arguments[0].ArgumentIndex);
        Assert.Equal(2, call.Arguments[1].ArgumentIndex);
        Assert.True(call.Arguments[0].HasArgumentLabel);
        Assert.True(call.Arguments[0].HasArgumentFlag);
        Assert.False(call.Arguments[0].ArgumentOptional);
    }
    /// <summary>A name nothing declares is pointed at, rather than discovered when the macro runs.</summary>
    [Theory]
    [InlineData("a + 1")]
    [InlineData("missing + 1")]
    public void A_name_no_variable_declares_is_marked(string formula)
    {
        var form = new ValuePickerViewModel(ValuePickerTests.Seed(formula), Variables(), ActionValueForm.Value);
        var name = Root(form).Items.OfType<SlotValue>().First(one => one.IsName);

        Assert.True(name.HasProblem);
        Assert.NotEmpty(name.ProblemHint);
    }

    /// <summary>A name the wall offers is not a mistake.</summary>
    [Fact]
    public void A_declared_name_is_not_marked()
    {
        var form = new ValuePickerViewModel(ValuePickerTests.Seed("count + 1"), Variables(), ActionValueForm.Value);

        Assert.DoesNotContain(form.Recipe!.Walk(), one => one.HasProblem);
    }
}
