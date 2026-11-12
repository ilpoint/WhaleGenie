using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// A number or a text typed in, and what happens when two pieces land together.
public partial class ValuePickerTests
{
    /// <summary>A number typed in the strip goes in as it reads.</summary>
    [Fact]
    public void A_number_typed_in_the_strip_goes_in_as_it_reads()
    {
        var form = new ValuePickerViewModel("$count >", Variables(), ActionReadMode.WholeValue);

        form.SetTypingMode(number: true);
        form.TypingText = "3.5";

        Assert.True(form.CanInsertTyped);
        Assert.Equal("3.5", form.TypedToken());
    }

    /// <summary>Text goes in quoted, with what is inside it escaped.</summary>
    [Fact]
    public void Text_is_put_in_between_quotes_with_what_is_inside_it_escaped()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), ActionReadMode.WholeValue);

        form.SetTypingMode(number: false);
        form.TypingText = "他说\"好\"";

        Assert.Equal("\"他说\\\"好\\\"\"", form.TypedToken());
    }

    /// <summary>A number that does not read is refused, and the row says why.</summary>
    [Theory]
    [InlineData("abc")]
    [InlineData("1e5")]
    [InlineData("1,000")]
    [InlineData("+5")]
    public void A_number_that_does_not_read_says_so_and_cannot_be_put_in(string typed)
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), ActionReadMode.WholeValue);

        form.SetTypingMode(number: true);
        form.TypingText = typed;

        Assert.Null(form.TypedToken());
        Assert.False(form.CanInsertTyped);
        Assert.True(form.HasTypingNote);
    }

    /// <summary>Whatever the field puts in as a number must be one number to the language too.</summary>
    [Theory]
    [InlineData("3.5")]
    [InlineData("0")]
    [InlineData("-5")]
    public void Every_number_that_can_be_inserted_reads_as_one_number(string typed)
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), ActionReadMode.WholeValue);

        form.SetTypingMode(number: true);
        form.TypingText = typed;

        var token = form.TypedToken();
        Assert.NotNull(token);
        Assert.True(Expression.TryParse(token, out var tree, out _, bare: true));
        Assert.True(tree is NumberNode or UnaryNode { Operator: "-", Operand: NumberNode });
    }

    /// <summary>An empty piece of text is not a material: there is nothing to put in.</summary>
    [Fact]
    public void An_empty_text_has_nothing_to_insert()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), ActionReadMode.WholeValue);

        form.SetTypingMode(number: false);

        Assert.Null(form.TypedToken());
        Assert.False(form.CanInsertTyped);
    }

    /// <summary>The strip offers brackets one character at a time and no quote button at all.</summary>
    [Fact]
    public void The_strip_offers_single_brackets_and_no_quote()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), ActionReadMode.WholeValue);

        var tokens = form.Operators.Select(one => one.Token).ToList();

        Assert.Contains("(", tokens);
        Assert.Contains(")", tokens);
        Assert.Contains("[", tokens);
        Assert.Contains("]", tokens);
        Assert.DoesNotContain("\"", tokens);
        Assert.DoesNotContain("( )", tokens);
    }

    /// <summary>
    /// Two numbers are two pieces, but putting one in front of a sum leaves "2 1 +", which reads as
    /// nothing. The move is refused rather than welding the line into a mistake.
    /// </summary>
    [Fact]
    public void Moving_a_number_in_front_of_a_sum_is_refused_because_it_no_longer_reads()
    {
        var form = new ValuePickerViewModel("1 + 2", Variables(), ActionReadMode.WholeValue,
            bareNames: true);
        var items = Root(form).Items;

        Assert.False(form.MoveOnto(items[2], items[0], after: false));

        Assert.Equal("1 + 2", form.Text);
    }

    /// <summary>A name is a word the same way a number is: the same move would stop the line reading.</summary>
    [Fact]
    public void Moving_a_name_in_front_of_a_sum_is_refused_because_it_no_longer_reads()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionReadMode.WholeValue,
            bareNames: true);
        var items = Root(form).Items;

        Assert.False(form.MoveOnto(items[2], items[0], after: false));

        Assert.Equal("$a + $b", form.Text);
    }

    /// <summary>Two quoted pieces side by side are not a formula either, so the move is refused.</summary>
    [Fact]
    public void Moving_a_string_beside_another_is_refused_because_it_no_longer_reads()
    {
        var form = new ValuePickerViewModel("\"x\" + \"y\"", Variables(), ActionReadMode.WholeValue,
            bareNames: true);
        var items = Root(form).Items;

        Assert.False(form.MoveOnto(items[2], items[0], after: false));

        Assert.Equal("\"x\" + \"y\"", form.Text);
    }

    /// <summary>
    /// Dropping a piece where it already stands is not a move. It used to leave "f(ab, , c)" behind:
    /// the piece welded itself to its left neighbour and kept the comma that was behind it.
    /// </summary>
    [Fact]
    public void Dropping_a_piece_where_it_already_stands_does_nothing()
    {
        var form = new ValuePickerViewModel("f(a, b, c)", Variables(), ActionReadMode.WholeValue);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.False(form.MoveOnto(call.Arguments[1].Items[0], call.Arguments[0].Items[0], after: true));
        Assert.Equal("f(a, b, c)", form.Text);
    }

    /// <summary>The ghost is a place like any other: a piece dropped on it joins the arguments.</summary>
    [Fact]
    public void A_piece_dropped_on_the_ghost_joins_the_arguments()
    {
        var form = new ValuePickerViewModel("concat($a) join($b)", Variables(), ActionReadMode.WholeValue);
        var root = Root(form);
        var call = root.Items.OfType<SlotCall>().First();

        Assert.True(form.MoveOnto(root.Items[^1], call.Ghost, after: false));

        Assert.Equal("concat($a, join($b))", form.Text);
    }

    /// <summary>
    /// A gap between pieces belongs to the run they sit in, so dropping there puts the piece at the
    /// end of that run rather than at the beginning of it.
    /// </summary>
    [Fact]
    public void A_piece_dropped_in_the_gap_joins_the_end_of_that_run()
    {
        var form = new ValuePickerViewModel("upper($a) lower($b)", Variables(), ActionReadMode.WholeValue);
        var root = Root(form);

        Assert.True(form.MoveOnto(root.Items[0], root, after: true));

        Assert.Equal("lower($b) upper($a)", form.Text);
    }
}
