using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

// What reaches the value window: a shape, never a line that has to be read back into one.
// The line under the band is only a picture of the shape, so these cases hold the window to
// handing the shape back exactly as it came in, identity included.
public partial class ValuePickerTests
{
    [Fact]
    public void A_shape_seed_opens_as_the_same_shape()
    {
        var shape = Formula.Binary("+", Formula.Name("count"), Formula.Number(2));

        Assert.Equal(shape, PickWithoutEditing(shape));
    }

    [Fact]
    public void A_call_seed_keeps_its_function_and_its_arguments()
    {
        var shape = Formula.Call("upper", Formula.Name("title"));

        AssertSameShape(shape, PickWithoutEditing(shape));
    }

    [Fact]
    public void A_run_seed_keeps_its_words_and_its_names()
    {
        var shape = Formula.Run(
        [
            Formula.Text("共 "),
            Formula.Name("count"),
            Formula.Text(" 件"),
        ]);

        AssertSameShape(shape, PickWithoutEditing(shape));
    }

    [Fact]
    public void A_shape_that_says_nothing_opens_an_empty_window()
    {
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(), ActionValueForm.Value);

        Assert.Equal(string.Empty, form.Text);
    }

    [Fact]
    public void The_line_under_the_band_is_drawn_from_the_shape()
    {
        var form = new ValuePickerViewModel(Formula.Name("count"), Variables(),
            ActionValueForm.Value);

        Assert.Equal("count", form.Text);
    }

    [Fact]
    public void A_number_cell_keeps_the_shape_the_window_handed_it()
    {
        var cell = new NumberCellViewModel("x", "hint");
        var shape = Formula.Binary("+", Formula.Name("count", "a7k2m9"), Formula.Number(2));

        cell.Write(shape);

        // The identity a bound name carries survives: the shape is kept, not a line that would
        // have to be read back and would lose which declaration the name belongs to.
        Assert.Equal(shape, cell.Seed);
    }

    [Fact]
    public void A_number_typed_into_a_cell_is_the_shape_the_cell_keeps()
    {
        var cell = new NumberCellViewModel("x", "hint");

        cell.Write("3");

        Assert.Equal(Formula.Number(3), cell.Seed);
    }

    /// <summary>
    /// A container is a record holding a list, so record equality would compare the lists by
    /// identity. Its children are the value, and each is compared as the shape it is.
    /// </summary>
    private static void AssertSameShape(ExpressionNode expected, ExpressionNode actual)
    {
        switch (expected)
        {
            case ConcatNode run:
                var parts = Assert.IsType<ConcatNode>(actual).Parts;
                Assert.Equal(run.Parts.Count, parts.Count);
                for (var index = 0; index < parts.Count; index++)
                {
                    AssertSameShape(run.Parts[index], parts[index]);
                }

                return;

            case ListNode list:
                var items = Assert.IsType<ListNode>(actual).Items;
                Assert.Equal(list.Items.Count, items.Count);
                for (var index = 0; index < items.Count; index++)
                {
                    AssertSameShape(list.Items[index], items[index]);
                }

                return;

            case CallNode call:
                var arguments = Assert.IsType<CallNode>(actual);
                Assert.Equal(call.Name, arguments.Name);
                Assert.Equal(call.Arguments.Count, arguments.Arguments.Count);
                for (var index = 0; index < arguments.Arguments.Count; index++)
                {
                    AssertSameShape(call.Arguments[index], arguments.Arguments[index]);
                }

                return;

            case BinaryNode binary:
                var pair = Assert.IsType<BinaryNode>(actual);
                Assert.Equal(binary.Operator, pair.Operator);
                AssertSameShape(binary.Left, pair.Left);
                AssertSameShape(binary.Right, pair.Right);
                return;

            default:
                Assert.Equal(expected, actual);
                return;
        }
    }

    [Fact]
    public void A_shape_opened_and_left_alone_keeps_the_identity_a_line_cannot_carry()
    {
        var shape = Formula.Name("count", "a7k2m9");
        var form = new ValuePickerViewModel(shape, Variables(), ActionValueForm.Value);
        ValuePick? answer = null;
        form.CloseRequested += pick => answer = pick;

        form.Confirm();

        var handed = Assert.IsType<ValuePick>(answer).Value;
        Assert.Equal(shape, handed);
        Assert.Equal("a7k2m9", Assert.IsType<VariableNode>(handed).Id);
    }

    [Fact]
    public void An_edit_replaces_the_shape_the_window_started_from()
    {
        var form = new ValuePickerViewModel(Formula.Name("count", "a7k2m9"),
            Variables(), ActionValueForm.Value);
        ValuePick? answer = null;
        form.CloseRequested += pick => answer = pick;

        form.Clear();
        form.Place("1");
        form.Confirm();

        Assert.Equal(Formula.Number(1), Assert.IsType<ValuePick>(answer).Value);
    }

    private static ExpressionNode PickWithoutEditing(ExpressionNode seed)
    {
        var form = new ValuePickerViewModel(seed, Variables(), ActionValueForm.Value);
        ValuePick? answer = null;
        form.CloseRequested += pick => answer = pick;

        form.Confirm();

        return Assert.IsType<ValuePick>(answer).Value;
    }
}
