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
        var form = new ValuePickerViewModel(null, Variables(), ActionValueForm.Value);

        var root = Root(form);

        var hole = Assert.IsType<SlotEmpty>(Assert.Single(root.Items));
        Assert.True(hole.IsEmpty);
    }

    /// <summary>
    /// Every piece written is a piece of the run — a name, a sign, a number, a bracket — in the order
    /// they stand, with nothing folded into a tree that the text does not have.
    /// </summary>
    [Fact]
    public void A_run_is_the_pieces_in_the_order_they_stand()
    {
        var form = new ValuePickerViewModel("$a + 1", Variables(), ActionValueForm.Value);

        var items = Root(form).Items;

        Assert.Collection(
            items,
            one => Assert.Equal("$a", Assert.IsType<SlotValue>(one).Text),
            one => Assert.Equal("+", Assert.IsType<SlotOperator>(one).Text),
            one => Assert.Equal("1", Assert.IsType<SlotValue>(one).Text));
        Assert.All(items, one => Assert.True(one.CanRemove));
    }

    /// <summary>A call is a container with one run per argument, and a ghost place after the last one.</summary>
    [Fact]
    public void A_call_holds_one_run_per_argument()
    {
        var form = new ValuePickerViewModel("concat($a, $b)", Variables(), ActionValueForm.Value);

        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Equal("concat", call.Name);
        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal("$a", Assert.IsType<SlotValue>(Assert.Single(call.Arguments[0].Items)).Text);
        Assert.Equal("$b", Assert.IsType<SlotValue>(Assert.Single(call.Arguments[1].Items)).Text);
        Assert.Equal(", ", call.Arguments[1].Before);
        Assert.NotNull(call.Ghost);
        Assert.False(call.Ghost.Required);
    }

    /// <summary>A function in an argument is just another piece of that run.</summary>
    [Fact]
    public void A_call_inside_an_argument_is_a_piece_of_that_run()
    {
        var form = new ValuePickerViewModel("concat($a, upper($b))", Variables(), ActionValueForm.Value);

        var outer = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        var inner = Assert.IsType<SlotCall>(Assert.Single(outer.Arguments[1].Items));

        Assert.Equal("upper", inner.Name);
        Assert.Equal("$b", Assert.IsType<SlotValue>(Assert.Single(inner.Arguments[0].Items)).Text);
    }

    /// <summary>An argument written empty is a place, red dashed, waiting to be filled.</summary>
    [Fact]
    public void An_argument_written_empty_is_a_place_waiting()
    {
        var form = new ValuePickerViewModel("contains($title, )", Variables(), ActionValueForm.Value);

        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Equal(2, call.Arguments.Count);
        var hole = Assert.IsType<SlotEmpty>(Assert.Single(call.Arguments[1].Items));
        Assert.True(hole.Required);
    }

    /// <summary>A call with nothing in it says so, and still offers the place to fill.</summary>
    [Fact]
    public void A_call_with_nothing_in_it_says_so_and_still_offers_the_place_to_fill()
    {
        var form = new ValuePickerViewModel("upper()", Variables(), ActionValueForm.Value);

        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Empty(call.Arguments);
        Assert.True(call.HasNoArguments);
        Assert.NotNull(call.Ghost);
        Assert.True(call.Ghost.Required);
    }

    /// <summary>
    /// A bracket nobody opened or closed is still a piece. Nothing is ever dropped for not reading:
    /// the line under the band says what the engine makes of it, the band shows what is there.
    /// </summary>
    [Fact]
    public void A_bracket_on_its_own_is_still_a_piece()
    {
        var form = new ValuePickerViewModel("123 (", Variables(), ActionValueForm.Value);

        var items = Root(form).Items;

        Assert.Equal(2, items.Count);
        Assert.Equal("123", Assert.IsType<SlotValue>(items[0]).Text);
        Assert.Equal("(", Assert.IsType<SlotBracket>(items[1]).Text);
        Assert.True(items[1].CanRemove);
    }

    /// <summary>A character nobody knows is a piece too, not a reason to stop reading.</summary>
    [Fact]
    public void A_character_nobody_knows_is_a_piece_too()
    {
        var form = new ValuePickerViewModel("$a ? $b", Variables(), ActionValueForm.Value);

        var items = Root(form).Items;

        Assert.Equal(3, items.Count);
        Assert.Equal("?", Assert.IsType<SlotOperator>(items[1]).Text);
    }

    /// <summary>
    /// The band and the engine cut the line in the same places. A piece edge that the scanner does
    /// not know about would be a place the two could disagree about what a word is.
    /// </summary>
    [Fact]
    public void Every_band_edge_is_an_engine_token_edge()
    {
        var form = new ValuePickerViewModel("concat($a, upper($b)) + 1", Variables(),
            ActionValueForm.Value, bareNames: true);
        var edges = ExpressionTokens.Scan(form.Text)
            .SelectMany(token => new[] { token.Start, token.End })
            .ToHashSet();

        Assert.All(form.Seams(), seam => Assert.Contains(seam, edges));
        Assert.All(form.Recipe!.Walk().Where(piece => piece.Length > 0), piece =>
        {
            Assert.Contains(piece.Start, edges);
            Assert.Contains(piece.Start + piece.Length, edges);
        });
    }

    /// <summary>A leaf piece is one token, never a stretch of two welded together.</summary>
    [Fact]
    public void A_piece_never_spans_two_engine_tokens()
    {
        var form = new ValuePickerViewModel("concat($a, upper($b)) + 1", Variables(),
            ActionValueForm.Value, bareNames: true);
        var tokens = ExpressionTokens.Scan(form.Text);

        Assert.All(
            form.Recipe!.Walk().Where(piece =>
                piece.Length > 0 && piece is SlotValue or SlotOperator or SlotBracket),
            piece =>
            {
                var covered = tokens
                    .Count(token => token.Start < piece.Start + piece.Length
                        && token.End > piece.Start);
                Assert.Equal(1, covered);
            });
    }
}
