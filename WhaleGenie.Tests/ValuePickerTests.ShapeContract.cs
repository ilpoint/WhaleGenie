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
        var form = Form("count");

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
    public void Taking_a_piece_away_leaves_a_hole_rather_than_a_torn_tree()
    {
        var form = Form("concat(a, b)");
        var first = form.Recipe!.Walk().OfType<SlotValue>()
            .First(value => value.Text == "a");

        Assert.True(form.RemoveArgument(first));

        Assert.False(form.CanConfirm);
        Assert.Contains(form.Recipe.Walk(), piece => piece is SlotEmpty);
        Assert.Contains(form.Recipe.Walk(), piece => piece is SlotValue value && value.Text == "b");
    }

    [Fact]
    public void Undo_brings_back_the_shape_and_redo_takes_it_away_again()
    {
        var form = Form("a");
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
        var form = Form("a");

        form.Clear();

        Assert.Equal(Formula.Nothing, Answer(form));
        Assert.True(form.CanUndo);
        form.Undo();
        Assert.Equal(Formula.Name("a"), Answer(form));
    }

    [Fact]
    public void The_band_is_drawn_from_the_shape_and_names_stand_in_this_windows_spelling()
    {
        var form = Form("a + 1");

        Assert.All(form.Recipe!.Walk(), piece => Assert.NotNull(piece.Path));
        Assert.Contains(form.Recipe.Walk().OfType<SlotValue>(),
            value => value.Text == "a" && value.IsName);
        Assert.Contains(form.Recipe.Walk().OfType<SlotOperator>(),
            sign => sign.Text == "+");
    }

    [Fact]
    public void A_typed_line_becomes_the_shape_once_and_the_line_is_only_a_picture()
    {
        var form = Form();

        form.Text = "a + 1";

        Assert.Equal(Formula.Binary("+", Formula.Name("a"), Formula.Number(1)), Answer(form));
    }

    [Fact]
    public void A_drag_moves_a_piece_to_another_place_in_the_shape()
    {
        var form = Form("concat(a, b)");
        var values = form.Recipe!.Walk().OfType<SlotValue>().ToList();
        var first = values.First(value => value.Text == "a");
        var last = values.Last(value => value.Text == "b");

        Assert.True(form.MoveOnto(first, last, after: true));

        Assert.Contains(form.Recipe.Walk().OfType<SlotValue>(), value => value.Text == "a");
        Assert.Contains(form.Recipe.Walk().OfType<SlotValue>(), value => value.Text == "b");
    }

    private static ValuePickerViewModel Form(string seed = "", bool bare = false)
        => new(seed, Variables(), ActionValueForm.Value, bare);

    private static ExpressionNode Answer(ValuePickerViewModel form)
    {
        ValuePick? pick = null;
        form.CloseRequested += value => pick = value;
        form.Confirm();
        return Assert.IsType<ValuePick>(pick).Value;
    }
}
