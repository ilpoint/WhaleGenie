using System.Collections.Generic;
using System.Linq;
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
        var form = new ValuePickerViewModel("$count >", Variables(), namesOnly: false);

        form.SetTypingMode(number: true);
        form.TypingText = "3.5";

        Assert.True(form.CanInsertTyped);
        Assert.Equal("3.5", form.TypedToken());
    }

    /// <summary>Text goes in quoted, with what is inside it escaped.</summary>
    [Fact]
    public void Text_is_put_in_between_quotes_with_what_is_inside_it_escaped()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);

        form.SetTypingMode(number: false);
        form.TypingText = "他说\"好\"";

        Assert.Equal("\"他说\\\"好\\\"\"", form.TypedToken());
    }

    /// <summary>A number that does not read is refused, and the row says why.</summary>
    [Fact]
    public void A_number_that_does_not_read_says_so_and_cannot_be_put_in()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);

        form.SetTypingMode(number: true);
        form.TypingText = "abc";

        Assert.Null(form.TypedToken());
        Assert.False(form.CanInsertTyped);
        Assert.True(form.HasTypingNote);
    }

    /// <summary>The strip offers brackets one character at a time and no quote button at all.</summary>
    [Fact]
    public void The_strip_offers_single_brackets_and_no_quote()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);

        var tokens = form.Operators.Select(one => one.Token).ToList();

        Assert.Contains("(", tokens);
        Assert.Contains(")", tokens);
        Assert.Contains("[", tokens);
        Assert.Contains("]", tokens);
        Assert.DoesNotContain("\"", tokens);
        Assert.DoesNotContain("( )", tokens);
    }

    /// <summary>
    /// Two numbers are two pieces, and dragging one beside the other must not weld them into a
    /// single number — which is exactly what plain string insertion used to do.
    /// </summary>
    [Fact]
    public void Two_numbers_dropped_together_stay_two_numbers()
    {
        var form = new ValuePickerViewModel("1 + 2", Variables(), namesOnly: false);
        var items = Root(form).Items;

        Assert.True(form.MoveOnto(items[2], items[0], after: false));

        Assert.Equal("2 1 +", form.Text);
    }

    /// <summary>A name is a word the same way a number is: two of them side by side would read as one.</summary>
    [Fact]
    public void Two_names_dropped_together_stay_two_names()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        var items = Root(form).Items;

        Assert.True(form.MoveOnto(items[2], items[0], after: false));

        Assert.Equal("$b $a +", form.Text);
    }

    /// <summary>Two pieces of text are quoted; nothing separates them but a space of their own.</summary>
    [Fact]
    public void Two_strings_dropped_together_stay_two_strings()
    {
        var form = new ValuePickerViewModel("\"x\" + \"y\"", Variables(), namesOnly: false);
        var items = Root(form).Items;

        Assert.True(form.MoveOnto(items[2], items[0], after: false));

        Assert.Equal("\"y\" \"x\" +", form.Text);
    }

    /// <summary>
    /// Dropping a piece where it already stands is not a move. It used to leave "f(ab, , c)" behind:
    /// the piece welded itself to its left neighbour and kept the comma that was behind it.
    /// </summary>
    [Fact]
    public void Dropping_a_piece_where_it_already_stands_does_nothing()
    {
        var form = new ValuePickerViewModel("f(a, b, c)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.False(form.MoveOnto(call.Arguments[1].Items[0], call.Arguments[0].Items[0], after: true));
        Assert.Equal("f(a, b, c)", form.Text);
    }

    /// <summary>The ghost is a place like any other: a piece dropped on it joins the arguments.</summary>
    [Fact]
    public void A_piece_dropped_on_the_ghost_joins_the_arguments()
    {
        var form = new ValuePickerViewModel("concat($a) join($b)", Variables(), namesOnly: false);
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
        var form = new ValuePickerViewModel("upper($a) lower($b)", Variables(), namesOnly: false);
        var root = Root(form);

        Assert.True(form.MoveOnto(root.Items[0], root, after: true));

        Assert.Equal("lower($b) upper($a)", form.Text);
    }
}
