using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

// Editing a value as a shape. The band, the caret and the answer all come from one ValueDraft;
// these cases hold the ways a person changes it — a palette token, a sign, a function card, a
// cross, a drag, undo and clear — to that one value rather than to a line that is read back.
public partial class ValuePickerTests
{
    [Theory]
    [InlineData("true", typeof(ConstantNode))]
    [InlineData("3", typeof(NumberNode))]
    [InlineData("who", typeof(VariableNode))]
    [InlineData("\"hello\"", typeof(TextNode))]
    public void A_palette_token_becomes_the_shape_it_stands_for(string token, System.Type kind)
    {
        var form = Form();

        form.Place(token);

        Assert.IsType(kind, Answer(form));
    }

    [Fact]
    public void An_operator_grows_a_binary_and_the_next_piece_fills_its_hole()
    {
        var form = Form(Formula.Name("count"));

        form.Place(" + ");
        Assert.False(form.CanConfirm, "An unfinished binary shape cannot be handed over yet.");

        form.Place("2");

        Assert.Equal(Formula.Binary("+", Formula.Name("count"), Formula.Number(2)), Answer(form));
    }

    [Fact]
    public void A_function_card_puts_a_call_and_the_next_piece_fills_its_argument()
    {
        var form = Form();

        form.PlaceCall("upper");
        Assert.False(form.CanConfirm);

        form.Place("who");

        AssertSameShape(Formula.Call("upper", Formula.Name("who")), Answer(form));
    }

    [Fact]
    public void Taking_a_piece_away_closes_the_gap_rather_than_leaving_a_hole()
    {
        var form = Form(Formula.Call("concat", Formula.Name("a"), Formula.Name("b")));
        var first = form.Recipe!.Walk().OfType<SlotValue>()
            .First(value => value.Text == "a");

        Assert.True(form.RemoveArgument(first));

        Assert.True(form.CanConfirm);
        Assert.DoesNotContain(form.Recipe.Walk(), piece => piece is SlotEmpty { IsGhost: false });
        Assert.Contains(form.Recipe.Walk(), piece => piece is SlotValue value && value.Text == "b");
        AssertSameShape(Formula.Call("concat", Formula.Name("b")), Answer(form));
    }

    /// <summary>The name's two text neighbours become one text run; no empty piece is left between.</summary>
    [Fact]
    public void Taking_a_name_out_of_a_sentence_joins_its_two_sides()
    {
        var form = Form(Formula.Run([Formula.Text("Hello "), Formula.Name("who"), Formula.Text("!")]));
        var name = form.Recipe!.Walk().OfType<SlotValue>().Single(value => value.IsName);

        Assert.True(form.RemoveArgument(name));

        Assert.Equal(Formula.Text("Hello !"), Answer(form));
        Assert.DoesNotContain(form.Recipe.Walk(), piece => piece is SlotEmpty);
    }

    /// <summary>Removing the last piece clears the cell instead of leaving an empty place behind.</summary>
    [Fact]
    public void Taking_the_only_piece_away_empties_the_cell()
    {
        var form = Form(Formula.Name("only"));

        var value = Assert.Single(form.Recipe!.Walk().OfType<SlotValue>());

        Assert.True(form.RemoveArgument(value));

        Assert.Equal(Formula.Nothing, Answer(form));
        Assert.DoesNotContain(form.Recipe.Walk(), piece => piece is SlotValue);
    }

    /// <summary>
    /// A call that loses one of its required arguments is not a complete value even though no
    /// `HoleNode` was written. The missing count has to block confirm just the same.
    /// </summary>
    [Fact]
    public void Removing_a_required_argument_blocks_confirm_until_it_is_filled()
    {
        var form = Form(Formula.Call("substring", Formula.Text("abc"), Formula.Number("1", 1d)));
        var text = form.Recipe!.Walk().OfType<SlotValue>()
            .First(value => value.Source is TextNode);

        Assert.True(form.RemoveArgument(text));

        Assert.False(form.CanConfirm);
        Assert.True(Assert.Single(form.Recipe.Walk().OfType<SlotCall>()).Ghost!.Required);

        form.Undo();
        Assert.True(form.CanConfirm);
    }

    [Fact]
    public void Undo_brings_back_the_shape_and_redo_takes_it_away_again()
    {
        var form = Form(Formula.Name("a"));
        form.Place(" + ");

        Assert.True(form.CanUndo);
        form.Undo();
        Assert.Equal(Formula.Name("a"), Answer(form));

        Assert.True(form.CanRedo);
        form.Redo();
        Assert.False(form.CanConfirm, "Redo brings back the unfinished binary with its hole.");

        // The caret came back on that hole, so the next piece completes it.
        form.Place("2");
        Assert.Equal(Formula.Binary("+", Formula.Name("a"), Formula.Number(2)), Answer(form));
    }

    [Fact]
    public void Clearing_is_one_step_in_the_same_shape_history()
    {
        var form = Form(Formula.Name("a"));

        form.Clear();

        Assert.Equal(Formula.Nothing, Answer(form));
        Assert.True(form.CanUndo);
        form.Undo();
        Assert.Equal(Formula.Name("a"), Answer(form));
    }

    [Fact]
    public void The_band_is_drawn_from_the_shape_and_names_stand_in_this_windows_spelling()
    {
        var form = Form(Formula.Binary("+", Formula.Name("a"), Formula.Number("1", 1d)));

        Assert.All(form.Recipe!.Walk(), piece => Assert.NotNull(piece.Path));
        Assert.Contains(form.Recipe.Walk().OfType<SlotValue>(),
            value => value.Text == "a" && value.IsName);
        var binary = Assert.Single(form.Recipe.Walk().OfType<SlotBinary>());
        Assert.Equal("+", binary.Operator);
    }

    [Fact]
    public void Pieces_put_in_one_at_a_time_grow_the_shape_and_the_line_follows_it()
    {
        var form = Form(Formula.Name("a"));

        form.Place(" + ");
        form.Place("1");

        Assert.Equal("a + 1", form.Text);
        Assert.Equal(Formula.Binary("+", Formula.Name("a"), Formula.Number(1)), Answer(form));
    }

    [Fact]
    public void A_drag_moves_a_piece_to_another_place_in_the_shape()
    {
        var form = Form(Formula.Call("concat", Formula.Name("a"), Formula.Name("b")));
        var values = form.Recipe!.Walk().OfType<SlotValue>().ToList();
        var first = values.First(value => value.Text == "a");
        var last = values.Last(value => value.Text == "b");

        Assert.True(form.MoveOnto(first, last, after: true));

        Assert.Contains(form.Recipe.Walk().OfType<SlotValue>(), value => value.Text == "a");
        Assert.Contains(form.Recipe.Walk().OfType<SlotValue>(), value => value.Text == "b");
    }

    private static ValuePickerViewModel Form(ExpressionNode? seed = null)
        => new(seed ?? Formula.Nothing, Variables(), ActionValueForm.Value);

    private static ExpressionNode Answer(ValuePickerViewModel form)
    {
        ValuePick? pick = null;
        form.CloseRequested += value => pick = value;
        form.Confirm();
        return Assert.IsType<ValuePick>(pick).Value;
    }
}
