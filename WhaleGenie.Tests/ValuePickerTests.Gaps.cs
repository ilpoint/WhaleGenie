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
}
