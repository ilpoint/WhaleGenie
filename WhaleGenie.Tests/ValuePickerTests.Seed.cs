using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

// What reaches the value window: a shape, never a line somebody has to read back into one.
// The window itself still edits a line, so the window's entry point draws the shape out once and
// the same reader takes it back. These cases hold that round trip to its promise in both spellings.
public partial class ValuePickerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_shape_seed_opens_as_the_same_shape(bool bareNames)
    {
        var shape = Formula.Binary("+", Formula.Name("count"), Formula.Number(2));

        Assert.Equal(shape, PickWithoutEditing(shape, bareNames));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_call_seed_keeps_its_function_and_its_arguments(bool bareNames)
    {
        var shape = Formula.Call("upper", Formula.Name("title"));

        AssertSameShape(shape, PickWithoutEditing(shape, bareNames));
    }

    [Fact]
    public void A_run_seed_keeps_its_words_and_its_names_when_the_window_reads_marks()
    {
        var shape = Formula.Run([
            Formula.Text("共 "),
            Formula.Name("count"),
            Formula.Text(" 件"),
        ]);

        // A bare-name window reads a formula, not a sentence: a run of words with names in it is
        // at home in the marked spelling, where the line is read back as the run it came from.
        AssertSameShape(shape, PickWithoutEditing(shape, bareNames: false));
    }

    [Fact]
    public void A_shape_that_says_nothing_opens_an_empty_window()
    {
        Assert.Equal(string.Empty, ValuePickerWindow.SeedLine(null, bareNames: false));
        Assert.Equal(string.Empty, ValuePickerWindow.SeedLine(Formula.Nothing, bareNames: true));
    }

    [Fact]
    public void The_two_spellings_are_the_ones_their_readers_take_back()
    {
        var shape = Formula.Name("count");

        Assert.Equal("$count", ValuePickerWindow.SeedLine(shape, bareNames: false));
        Assert.Equal("count", ValuePickerWindow.SeedLine(shape, bareNames: true));
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

    private static ExpressionNode PickWithoutEditing(ExpressionNode seed, bool bareNames)
    {
        var form = new ValuePickerViewModel(ValuePickerWindow.SeedLine(seed, bareNames),
            Variables(), ActionValueForm.Value, bareNames);
        ValuePick? answer = null;
        form.CloseRequested += pick => answer = pick;

        form.Confirm();

        return Assert.IsType<ValuePick>(answer).Value;
    }
}
