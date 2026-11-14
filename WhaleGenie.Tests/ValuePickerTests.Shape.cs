using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// What the formula is made of: runs, calls, brackets and a character nobody knows.
public partial class ValuePickerTests
{
    [Fact]
    public void An_empty_formula_is_one_place_to_start_from()
    {
        var form = new ValuePickerViewModel((string?)null, Variables(), ActionValueForm.Value);

        var root = Root(form);

        var hole = Assert.IsType<SlotEmpty>(Assert.Single(root.Items));
        Assert.True(hole.IsEmpty);
    }
    /// <summary>A call is a container with one run per argument, and a ghost place after the last one.</summary>
    [Fact]
    public void A_call_holds_one_run_per_argument()
    {
        var form = new ValuePickerViewModel("concat(a, b)", Variables(), ActionValueForm.Value);

        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Equal("concat", call.Name);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal("a", Assert.IsType<SlotValue>(Assert.Single(call.Arguments[0].Items)).Text);
        Assert.Equal("b", Assert.IsType<SlotValue>(Assert.Single(call.Arguments[1].Items)).Text);
        Assert.Equal(", ", call.Arguments[1].Before);
        Assert.NotNull(call.Ghost);
        Assert.False(call.Ghost.Required);
    }

    /// <summary>A function in an argument is just another piece of that run.</summary>
    [Fact]
    public void A_call_inside_an_argument_is_a_piece_of_that_run()
    {
        var form = new ValuePickerViewModel("concat(a, upper(b))", Variables(), ActionValueForm.Value);

        var outer = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        var inner = Assert.IsType<SlotCall>(Assert.Single(outer.Arguments[1].Items));

        Assert.Equal("upper", inner.Name);
        Assert.Equal("b", Assert.IsType<SlotValue>(Assert.Single(inner.Arguments[0].Items)).Text);
    }
}
