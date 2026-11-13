using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// A piece written at the caret without taking over its neighbour.
public partial class ValuePickerTests
{
    [Fact]
    public void A_piece_put_before_a_name_does_not_take_it_over()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionValueForm.Value);
        form.SetCaret(5);

        form.Place("$c");

        Assert.Equal("$a + $c $b", form.Text);
    }

    /// <summary>A call added at the front must not fuse with the name that follows it.</summary>
    [Fact]
    public void A_call_put_at_the_front_does_not_fuse_with_the_next_piece()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionValueForm.Value);
        form.SetCaret(0);

        form.PlaceCall("upper");

        Assert.Equal("upper() $a + $b", form.Text);
    }

    /// <summary>The standing field opens ready for a number, the more common thing to type.</summary>
    [Fact]
    public void The_small_field_opens_in_number_mode()
    {
        var form = new ValuePickerViewModel("$a", Variables(), ActionValueForm.Value);

        Assert.True(form.IsTypingNumber);
        Assert.False(form.IsTypingText);
    }

    /// <summary>A number and a piece of text are written differently, so switching forgets the half-typed one.</summary>
    [Fact]
    public void Switching_the_field_mode_clears_what_was_typed()
    {
        var form = new ValuePickerViewModel("$a", Variables(), ActionValueForm.Value);
        form.TypingText = "12.5";

        form.SetTypingMode(number: false);

        Assert.True(form.IsTypingText);
        Assert.Equal(string.Empty, form.TypingText);
        form.TypingText = "hello";
        Assert.Equal("\"hello\"", form.TypedToken());
    }

    [Fact]
    public void Clearing_the_field_leaves_it_ready_for_the_next_value()
    {
        var form = new ValuePickerViewModel("$a", Variables(), ActionValueForm.Value);
        form.TypingText = "42";

        form.ClearTyped();

        Assert.Equal(string.Empty, form.TypingText);
        Assert.False(form.HasTypingNote);
        Assert.False(form.CanInsertTyped);
    }

    /// <summary>A space already standing at the seam is the separator; a second one would leave a gap.</summary>
    [Fact]
    public void A_space_already_at_the_seam_is_not_doubled()
    {
        var form = new ValuePickerViewModel("42 $a", Variables(), ActionValueForm.Value);
        form.SetCaret(2);

        form.Place("\"hello\"");

        Assert.Equal("42 \"hello\" $a", form.Text);
    }
}
