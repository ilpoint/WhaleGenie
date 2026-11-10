using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Taking a piece away, and putting one on the end or in front of an argument.
public partial class ValuePickerTests
{
    /// <summary>Taking a piece away takes it and the comma that kept it from its neighbour.</summary>
    [Fact]
    public void Taking_a_piece_away_takes_its_comma_too()
    {
        var form = new ValuePickerViewModel("concat($a, $b, $c)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(form.RemoveArgument(call.Arguments[1].Items[0]));

        Assert.Equal("concat($a, $c)", form.Text);
    }

    /// <summary>Taking the first piece away takes the comma behind it instead.</summary>
    [Fact]
    public void Taking_the_first_piece_away_keeps_the_rest_whole()
    {
        var form = new ValuePickerViewModel("concat($a, $b)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(form.RemoveArgument(call.Arguments[0].Items[0]));

        Assert.Equal("concat($b)", form.Text);
    }

    /// <summary>A sign goes on its own; what is left may not read, and the line under says so.</summary>
    [Fact]
    public void A_sign_can_be_taken_away_on_its_own()
    {
        var form = new ValuePickerViewModel("$a and $b", Variables(), namesOnly: false);

        Assert.True(form.RemoveArgument(Root(form).Items[1]));

        Assert.Equal("$a $b", form.Text);
    }

    /// <summary>
    /// A function card adds an empty call of its own. It never puts itself around a piece already
    /// written: taking over what the person had built was the reason for removing wrapping.
    /// </summary>
    [Fact]
    public void A_function_card_does_not_take_over_a_piece()
    {
        var form = new ValuePickerViewModel("$title", Variables(), namesOnly: false);

        form.PlaceCall("upper");

        Assert.Equal("$title upper()", form.Text);
    }

    /// <summary>A piece added to the line goes on its end, leaving what is written alone.</summary>
    [Fact]
    public void A_piece_goes_on_the_end_of_the_line()
    {
        var form = new ValuePickerViewModel("$a +", Variables(), namesOnly: false);

        form.Place("1");

        Assert.Equal("$a + 1", form.Text);
    }

    /// <summary>Dragging one argument onto another puts it there, with the commas put right.</summary>
    [Fact]
    public void A_piece_dropped_on_the_first_argument_goes_in_front_of_it()
    {
        var form = new ValuePickerViewModel("concat($a, $b, $c)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(form.MoveOnto(call.Arguments[2].Items[0], call.Arguments[0].Items[0], after: false));

        Assert.Equal("concat($c, $a, $b)", form.Text);
    }

    /// <summary>A piece can be dragged from one call into another.</summary>
    [Fact]
    public void A_piece_can_be_dragged_from_one_call_into_another()
    {
        var form = new ValuePickerViewModel("concat($a, $b) and join($c)", Variables(), namesOnly: false);
        var calls = Root(form).Items.OfType<SlotCall>().ToList();

        Assert.True(form.MoveOnto(calls[1].Arguments[0].Items[0], calls[0].Arguments[1].Items[0], false));

        Assert.Equal("concat($a, $c, $b) and join()", form.Text);
    }

    /// <summary>Undo goes back a step at a time, and redo returns to where it was.</summary>
    [Fact]
    public void Undo_and_redo_walk_the_line()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);

        form.Text = "$a and $b";
        form.Text = "$a and $b and $c";

        Assert.True(form.CanUndo);

        form.Undo();
        Assert.Equal("$a and $b", form.Text);

        form.Undo();
        Assert.Equal("$a", form.Text);
        Assert.False(form.CanUndo);

        form.Redo();
        Assert.Equal("$a and $b", form.Text);
        Assert.True(form.CanRedo);
    }
}
