using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Where the caret sits, what it does when the formula changes, and the space it leaves.
public partial class ValuePickerTests
{
    [Fact]
    public void The_caret_is_clamped_to_the_formula()
    {
        var form = new ValuePickerViewModel(Seed(Formula.Name("a")), Variables(), ActionValueForm.Value);

        form.SetCaret(-20);
        Assert.Equal(0, form.Caret);

        form.SetCaret(500);
        Assert.Equal(form.Text.Length, form.Caret);
    }

    /// <summary>Emptying the shape must not leave the caret pointing past the line it draws.</summary>
    [Fact]
    public void Clearing_the_shape_pulls_the_caret_back_with_it()
    {
        var form = new ValuePickerViewModel(Seed(Formula.Binary("+", Formula.Binary("+", Formula.Name("a"), Formula.Name("b")), Formula.Name("c"))), Variables(), ActionValueForm.Value);
        form.SetCaret(form.Text.Length);

        form.Clear();

        Assert.Equal(form.Text.Length, form.Caret);
        Assert.Equal(0, form.Caret);
    }
    /// <summary>The ghost is still the way to add an argument now that the plus button is gone.</summary>
    [Fact]
    public void Filling_the_ghost_writes_its_comma_and_grows_another_one()
    {
        var form = new ValuePickerViewModel(Seed(Formula.Call("concat", Formula.Name("a"))), Variables(), ActionValueForm.Value);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        form.SetCaret(call.Ghost!.Start);
        form.Place("b");

        Assert.Equal("concat(a, b)", form.Text);
        var after = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        Assert.Equal(2, after.Arguments.Count);
        Assert.NotNull(after.Ghost);
    }
    /// <summary>The band is rebuilt after every edit; the caret must still land inside its line.</summary>
    [Fact]
    public void The_caret_survives_a_rebuilt_band()
    {
        var form = new ValuePickerViewModel(Seed(Formula.Binary("+", Formula.Name("a"), Formula.Name("b"))), Variables(), ActionValueForm.Value);
        form.SetCaret(2);

        form.Place(" + ");
        form.Place("c");

        Assert.InRange(form.Caret, 0, form.Text.Length);
    }
    /// <summary>Writing at a caret that was never a seam must not split the piece it landed in.</summary>
    [Fact]
    public void An_insert_never_splits_a_token()
    {
        var form = new ValuePickerViewModel(Seed(Formula.Name("photos")), Variables(), ActionValueForm.Value);
        form.SetCaret(3);

        form.Place("x");

        Assert.Contains(Root(form).Items.OfType<SlotValue>(), one => one.Text == "photos");
    }

    /// <summary>The end of a run is after its last piece, not before it.</summary>
    [Fact]
    public void The_end_of_a_run_appends_instead_of_inserting_before_the_last()
    {
        var form = Form(Formula.Run([Formula.Text("a"), Formula.Name("b")]));
        form.SetCaret(form.SpanEnd);

        form.Place("x");

        Assert.Equal(["a", "b", "x"], Root(form).Items.OfType<SlotValue>().Select(value => value.Text));
        Assert.Equal(form.SpanEnd, form.Caret);
    }

    /// <summary>The operator at the end wraps the whole run, and its new hole disables it again.</summary>
    [Fact]
    public void An_operator_at_the_end_wraps_the_whole_run()
    {
        var form = Form(Formula.Run([Formula.Text("a"), Formula.Name("b")]));
        form.SetCaret(form.SpanEnd);

        form.Place("x");
        form.Place(" + ");

        var binary = Assert.IsType<BinaryNode>(
            Assert.Single(form.Recipe!.Walk().OfType<SlotBinary>()).Source);
        AssertSameShape(Formula.Run([Formula.Text("a"), Formula.Name("b"), Formula.Name("x")]), binary.Left);
        Assert.IsType<HoleNode>(binary.Right);
        Assert.All(form.Operators, one => Assert.False(one.IsEnabled));

        form.Place("1");
        Assert.Contains(form.Recipe!.Walk().OfType<SlotValue>(), one => one.Text == "1");
    }

    /// <summary>There is no left value to wrap, so an operator on an empty value must not grow a hole.</summary>
    [Fact]
    public void An_operator_without_a_left_value_is_unavailable()
    {
        var form = Form();

        Assert.All(form.Operators, one => Assert.False(one.IsEnabled));

        form.Place(" + ");

        Assert.Equal(string.Empty, form.Text);
        Assert.All(form.Operators, one => Assert.False(one.IsEnabled));
    }

    /// <summary>A function at the end is appended; the last existing piece survives.</summary>
    [Fact]
    public void A_function_at_the_end_is_appended_instead_of_replacing_the_last_piece()
    {
        var form = Form(Formula.Run([Formula.Text("a"), Formula.Name("b")]));
        form.SetCaret(form.SpanEnd);

        form.PlaceCall("upper");

        var items = Root(form).Items;
        Assert.Equal(3, items.Count);
        Assert.Contains(items, one => one is SlotValue { Text: "b" });
        var call = Assert.IsType<SlotCall>(items[2]);
        Assert.Equal("upper", call.Name);
        Assert.IsType<SlotEmpty>(Assert.Single(call.Arguments).Items.Single());
    }

    /// <summary>Backspace at the end of a call takes its last argument, never the whole call.</summary>
    [Fact]
    public void Backspace_at_a_call_end_removes_only_the_last_argument()
    {
        var form = Form(Formula.Call("concat", Formula.Name("a"), Formula.Name("b")));
        form.SetCaret(form.SpanEnd);

        Assert.True(form.CutBeforeCaret());

        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        Assert.Equal("a", Assert.IsType<SlotValue>(Assert.Single(call.Arguments[0].Items)).Text);
        Assert.Single(call.Arguments);
        // The next argument's socket is where the line now stands, not outside the closing bracket.
        Assert.Equal(call.Ghost!.Start, form.Caret);
    }

    /// <summary>The inside of a binary is one structural unit; only its outside and holes are writable.</summary>
    [Fact]
    public void A_binary_offers_only_its_outside_edges_and_its_holes_as_seams()
    {
        var form = Form(Formula.Binary("+", Formula.Name("a"), Formula.Name("b")));

        Assert.Equal([0, 3], form.Seams());

        form.SetCaret(2);
        Assert.Equal(3, form.Caret);
    }
}
