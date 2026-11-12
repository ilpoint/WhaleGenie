using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Folding a long call to fit the width the band has.
public partial class ValuePickerTests
{

    private static SlotCall OnlyCall(ValuePickerViewModel form)
        => Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

    /// <summary>More than three arguments get one row each; a short call stays on one line.</summary>
    [Fact]
    public void SlotStackingExpandsForManyArguments()
    {
        var many = OnlyCall(new ValuePickerViewModel("concat($a, $b, $c, $d)", Variables(), ActionReadMode.WholeValue));
        var few = OnlyCall(new ValuePickerViewModel("concat($a, $b)", Variables(), ActionReadMode.WholeValue));

        Assert.True(many.Stacked);
        Assert.False(few.Stacked);
        Assert.True(few.Compact);
    }

    /// <summary>
    /// A nested call used to unfold its parent on its own, which doubled the band for a formula
    /// like get(list(1, 2), 2). Nesting alone is no longer a reason; width or argument count is.
    /// </summary>
    [Fact]
    public void SlotStackingKeepsANestedCallOnOneLineWhenItFits()
    {
        var call = OnlyCall(new ValuePickerViewModel("get(list(1, 2), 2)", Variables(), ActionReadMode.WholeValue));
        Assert.False(call.Stacked);
        Assert.True(call.Compact);
    }

    /// <summary>A short formula, and a formula with no call at all, keep the compact row.</summary>
    [Fact]
    public void SlotStackingKeepsShortFormulaOnOneLine()
    {
        Assert.False(OnlyCall(new ValuePickerViewModel("upper($a)", Variables(), ActionReadMode.WholeValue)).Stacked);
        var plain = new ValuePickerViewModel("1 + 2", Variables(), ActionReadMode.WholeValue);
        Assert.Empty(Root(plain).Items.OfType<SlotCall>());
    }

    /// <summary>The same call folds when the band is narrow and opens out when it is wide.</summary>
    [Fact]
    public void SlotStackingUsesAvailableWidth()
    {
        // Long enough that the smaller per-character estimate still crosses a 200 pixel band.
        var form = new ValuePickerViewModel("concat($alpha, $beta)", Variables(), ActionReadMode.WholeValue);
        var call = OnlyCall(form);

        form.RefreshStacking(200);
        Assert.True(call.Stacked);

        form.RefreshStacking(1080);
        Assert.False(call.Stacked);
    }

    /// <summary>Only one level folds: a nested call already has a row of its own to lie on.</summary>
    [Fact]
    public void OnlyOneLevelOfCallsFoldsAtATime()
    {
        // Four arguments are what makes the outer call fold now; the nested calls must stay compact.
        var form = new ValuePickerViewModel("concat(upper($a), lower($b), 1, 2)", Variables(), ActionReadMode.WholeValue);
        form.RefreshStacking(1080);

        var outer = OnlyCall(form);
        var nested = outer.Arguments.SelectMany(argument => argument.Items.OfType<SlotCall>()).ToList();
        Assert.True(outer.Stacked);
        Assert.Equal(2, nested.Count);
        Assert.All(nested, call => Assert.False(call.Stacked));
    }

    /// <summary>A folded call reads top to bottom when deciding which side of it to drop on.</summary>
    [Fact]
    public void A_stacked_call_reads_top_to_bottom()
    {
        var call = OnlyCall(new ValuePickerViewModel("concat($a, $b, $c, $d)", Variables(), ActionReadMode.WholeValue));
        Assert.True(call.Stacked);

        Assert.True(ValuePickerViewModel.AfterFor(call, rightHalf: false, lowerHalf: true));
        Assert.False(ValuePickerViewModel.AfterFor(call, rightHalf: true, lowerHalf: false));
    }
}
