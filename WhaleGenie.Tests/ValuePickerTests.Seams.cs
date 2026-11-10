using System.Collections.Generic;
using System.Linq;
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

    /// <summary>Taking a sign out from between two numbers must not weld them into one.</summary>
    [Fact]
    public void Taking_a_sign_out_from_between_two_numbers_does_not_weld_them()
    {
        var form = new ValuePickerViewModel("1-2", Variables(), namesOnly: false);

        Assert.True(form.RemoveArgument(Root(form).Items[1]));

        Assert.Equal("1 2", form.Text);
    }

    /// <summary>A cross belongs to the piece the pointer is on, not to everything it sits inside.</summary>
    [Fact]
    public void A_cross_shows_only_where_the_pointer_is()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        var value = Root(form).Items[0];

        Assert.True(value.CanRemove);
        Assert.False(value.ShowsRemove);

        value.IsHovered = true;
        Assert.True(value.ShowsRemove);

        value.IsHovered = false;
        Assert.False(value.ShowsRemove);
    }

    /// <summary>
    /// The band draws its cursor from the preview and applies the same thing on release, so the two
    /// cannot disagree — and a spot the piece already fills is reported as "nothing would happen".
    /// </summary>
    [Fact]
    public void A_preview_says_where_the_piece_would_land()
    {
        var form = new ValuePickerViewModel("concat($a, $b)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        var onto = form.PreviewMove(call.Arguments[1].Items[0], call.Arguments[0].Items[0], after: false);
        Assert.True(onto.Allowed);
        Assert.False(onto.NoOp);
        Assert.Same(call.Arguments[0].Items[0], onto.Anchor);

        var same = form.PreviewMove(call.Arguments[1].Items[0], call.Arguments[0].Items[0], after: true);
        Assert.True(same.NoOp);
        Assert.False(same.Allowed);
    }

    /// <summary>
    /// A drop into the space of a run belongs after its last piece. The cursor has to be drawn on
    /// that piece, not on the whole run the pointer happened to land in.
    /// </summary>
    [Fact]
    public void A_drop_in_a_run_points_at_its_last_piece()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        var root = Root(form);
        var last = root.Items.Last(one => one.Length > 0);

        var preview = form.PreviewMove(root.Items[0], root, after: false);

        Assert.Same(last, preview.Anchor);
        Assert.True(preview.After);
    }

    /// <summary>A run that folds reads top to bottom; a single piece still reads left to right.</summary>
    [Fact]
    public void A_folded_run_reads_top_to_bottom()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        var root = Root(form);
        var value = root.Items[0];

        Assert.True(ValuePickerViewModel.AfterFor(root, rightHalf: false, lowerHalf: true));
        Assert.False(ValuePickerViewModel.AfterFor(root, rightHalf: true, lowerHalf: false));
        Assert.True(ValuePickerViewModel.AfterFor(value, rightHalf: true, lowerHalf: false));
        Assert.False(ValuePickerViewModel.AfterFor(value, rightHalf: false, lowerHalf: true));
    }
}
