using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Where the caret sits, what it does when the formula changes, and the space it leaves.
public partial class ValuePickerTests
{
    /// <summary>The insertion point starts where a formula being built a piece at a time goes on.</summary>
    [Fact]
    public void The_caret_starts_at_the_end_of_the_formula()
    {
        var form = new ValuePickerViewModel("$a + 1", Variables(), namesOnly: false);

        Assert.Equal(form.Text.Length, form.Caret);
    }

    [Fact]
    public void The_caret_is_clamped_to_the_formula()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);

        form.SetCaret(-20);
        Assert.Equal(0, form.Caret);

        form.SetCaret(500);
        Assert.Equal(form.Text.Length, form.Caret);
    }

    /// <summary>Reading a shorter line again must not leave the line pointing past its end.</summary>
    [Fact]
    public void A_shorter_formula_pulls_the_caret_back_with_it()
    {
        var form = new ValuePickerViewModel("$a + $b + $c", Variables(), namesOnly: false);
        form.SetCaret(form.Text.Length);

        form.Text = "$a";

        Assert.Equal(form.Text.Length, form.Caret);
    }

    /// <summary>Writing in the middle adds a piece; it never takes over the pieces on either side.</summary>
    [Fact]
    public void A_piece_written_at_the_caret_leaves_the_rest_alone()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        form.SetCaret(2);

        form.Place("$c");

        Assert.Equal("$a $c + $b", form.Text);
        Assert.Equal(5, form.Caret);
    }

    [Fact]
    public void Two_word_pieces_meeting_at_the_caret_get_a_space()
    {
        var form = new ValuePickerViewModel("count", Variables(), namesOnly: false);
        form.SetCaret(0);

        form.Place("$a");

        Assert.Equal("$a count", form.Text);
    }

    /// <summary>A function card leaves the line between its brackets, ready for the first argument.</summary>
    [Fact]
    public void A_function_card_leaves_the_caret_between_its_brackets()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);

        form.PlaceCall("upper");

        Assert.Equal("upper()", form.Text);
        Assert.Equal("upper(".Length, form.Caret);
    }

    [Fact]
    public void The_next_piece_goes_inside_a_call_the_card_just_wrote()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);
        form.PlaceCall("upper");

        form.Place("$a");

        Assert.Equal("upper($a)", form.Text);
    }

    /// <summary>The ghost is still the way to add an argument now that the plus button is gone.</summary>
    [Fact]
    public void Filling_the_ghost_writes_its_comma_and_grows_another_one()
    {
        var form = new ValuePickerViewModel("concat($a)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        form.SetCaret(call.Ghost!.Start);
        form.Place("$b");

        Assert.Equal("concat($a, $b)", form.Text);
        var after = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        Assert.Equal(2, after.Arguments.Count);
        Assert.NotNull(after.Ghost);
    }

    /// <summary>A bracket from the strip writes one character and nothing is put inside a pair.</summary>
    [Fact]
    public void A_single_bracket_is_written_as_one_character()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);
        form.SetCaret(0);

        form.Place("(");

        Assert.Equal("($a", form.Text);
    }

    /// <summary>The band is rebuilt after every edit; the insertion point is a character offset, so it stays.</summary>
    [Fact]
    public void The_caret_survives_a_rebuilt_band()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        form.SetCaret(2);

        form.Text = "$a + $b + $c";

        Assert.Equal(2, form.Caret);
    }
}
