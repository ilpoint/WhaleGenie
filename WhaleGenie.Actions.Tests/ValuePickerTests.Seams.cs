using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Where a piece would land: the seams a cut moves, the cross and the preview.
public partial class ValuePickerTests
{
    /// <summary>A seam is measured before the cut and has to move by whatever the cut actually took.</summary>
    [Fact]
    public void A_seam_before_a_cut_moves_by_what_the_cut_took()
    {
        var span = new ValuePickerViewModel.CutSpan(3, 6);

        Assert.Equal(2, ValuePickerViewModel.SeamAfterCut(2, span));
        Assert.Equal(3, ValuePickerViewModel.SeamAfterCut(4, span));
        Assert.Equal(3, ValuePickerViewModel.SeamAfterCut(6, span));
        Assert.Equal(4, ValuePickerViewModel.SeamAfterCut(7, span));
    }
    /// <summary>A cross belongs to the piece the pointer is on, not to everything it sits inside.</summary>
    [Fact]
    public void A_cross_shows_only_where_the_pointer_is()
    {
        var form = new ValuePickerViewModel(Seed(Formula.Binary("+", Formula.Name("a"), Formula.Name("b"))), Variables(), ActionValueForm.Value);
        var value = Root(form).Items[0];

        Assert.True(value.CanRemove);
        Assert.False(value.ShowsRemove);

        value.IsHovered = true;
        Assert.True(value.ShowsRemove);

        value.IsHovered = false;
        Assert.False(value.ShowsRemove);
    }
    /// <summary>A run that folds reads top to bottom; a single piece still reads left to right.</summary>
    [Fact]
    public void A_folded_run_reads_top_to_bottom()
    {
        var form = new ValuePickerViewModel(Seed(Formula.Binary("+", Formula.Name("a"), Formula.Name("b"))), Variables(), ActionValueForm.Value);
        var root = Root(form);
        var value = root.Items[0];

        Assert.True(ValuePickerViewModel.AfterFor(root, rightHalf: false, lowerHalf: true));
        Assert.False(ValuePickerViewModel.AfterFor(root, rightHalf: true, lowerHalf: false));
        Assert.True(ValuePickerViewModel.AfterFor(value, rightHalf: true, lowerHalf: false));
        Assert.False(ValuePickerViewModel.AfterFor(value, rightHalf: false, lowerHalf: true));
    }
}
