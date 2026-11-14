using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Where the caret sits, what it does when the formula changes, and the space it leaves.
public partial class ValuePickerTests
{
    [Fact]
    public void The_caret_is_clamped_to_the_formula()
    {
        var form = new ValuePickerViewModel("$a", Variables(), ActionValueForm.Value);

        form.SetCaret(-20);
        Assert.Equal(0, form.Caret);

        form.SetCaret(500);
        Assert.Equal(form.Text.Length, form.Caret);
    }

    /// <summary>Reading a shorter line again must not leave the line pointing past its end.</summary>
    [Fact]
    public void A_shorter_formula_pulls_the_caret_back_with_it()
    {
        var form = new ValuePickerViewModel("$a + $b + $c", Variables(), ActionValueForm.Value);
        form.SetCaret(form.Text.Length);

        form.Text = "$a";

        Assert.Equal(form.Text.Length, form.Caret);
    }
    /// <summary>The ghost is still the way to add an argument now that the plus button is gone.</summary>
    [Fact]
    public void Filling_the_ghost_writes_its_comma_and_grows_another_one()
    {
        var form = new ValuePickerViewModel("concat($a)", Variables(), ActionValueForm.Value);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        form.SetCaret(call.Ghost!.Start);
        form.Place("$b");

        Assert.Equal("concat(「a」, 「b」)", form.Text);
        var after = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        Assert.Equal(2, after.Arguments.Count);
        Assert.NotNull(after.Ghost);
    }
    /// <summary>The band is rebuilt after every edit; the insertion point is a character offset, so it stays.</summary>
    [Fact]
    public void The_caret_survives_a_rebuilt_band()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionValueForm.Value);
        form.SetCaret(2);

        form.Text = "$a + $b + $c";

        Assert.Equal(2, form.Caret);
    }

    /// <summary>A place inside a name is not a place the band can draw, so the line goes to an edge.</summary>
    [Fact]
    public void A_caret_asked_for_inside_a_piece_lands_on_its_edge()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionValueForm.Value);

        form.SetCaret(1);
        Assert.Equal(0, form.Caret);

        form.SetCaret(6);
        Assert.Equal(5, form.Caret);
    }

    /// <summary>Writing at a caret that was never a seam must not split the piece it landed in.</summary>
    [Fact]
    public void An_insert_never_splits_a_token()
    {
        var form = new ValuePickerViewModel("photos", Variables(), ActionValueForm.Value);
        form.SetCaret(3);

        form.Place("$x");

        Assert.Contains(Root(form).Items.OfType<SlotValue>(), one => one.Text == "photos");
    }
}
