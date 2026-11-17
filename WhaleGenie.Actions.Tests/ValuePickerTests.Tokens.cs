using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

// The band as a tree of tokens: a sign is inside its expression, brackets are one token, and a
// value with no socket does not grow a sibling.
public partial class ValuePickerTests
{
    [Fact]
    public void A_sign_is_inside_the_binary_token_and_the_missing_operand_is_its_socket()
    {
        var form = Form(Formula.Binary("+", Formula.Name("a"), Formula.Hole));

        var binary = Assert.Single(form.Recipe!.Walk().OfType<SlotBinary>());
        Assert.Equal("+", binary.Operator);
        Assert.IsType<SlotValue>(binary.Left);
        Assert.IsType<SlotEmpty>(binary.Right);
        Assert.DoesNotContain(form.Recipe.Walk(), piece => piece is SlotBinary && piece.Path is null);
    }

    [Fact]
    public void Brackets_wrap_a_picked_token_as_one_group()
    {
        var form = Form(Formula.Binary("+", Formula.Name("a"), Formula.Number("1", 1d)));
        var left = form.Recipe!.Walk().OfType<SlotValue>().First(value => value.Text == "a");

        form.Select(left);
        Assert.True(form.WrapGroup());

        var binary = Assert.IsType<BinaryNode>(Answer(form));
        Assert.IsType<GroupNode>(binary.Left);
        Assert.Contains(form.Recipe!.Walk(), piece => piece is SlotGroup);
        Assert.IsType<SlotGroup>(form.Recipe!.Walk().OfType<SlotGroup>().Single());
    }

    [Fact]
    public void Brackets_wrap_the_whole_value_when_nothing_is_picked()
    {
        var form = Form(Formula.Binary("+", Formula.Name("a"), Formula.Number("1", 1d)));

        Assert.True(form.WrapGroup());

        Assert.IsType<GroupNode>(Answer(form));
        var root = Assert.IsType<SlotRoot>(form.Recipe);
        Assert.IsType<SlotGroup>(root.Child);
    }

    [Fact]
    public void A_list_is_one_token_with_its_items_and_a_ghost()
    {
        var form = Form(Formula.List(Formula.Number("1", 1d), Formula.Number("2", 2d)));

        var list = Assert.Single(form.Recipe!.Walk().OfType<SlotList>());
        Assert.Equal(2, list.Items.Count);
        Assert.NotNull(list.Ghost);
        Assert.Equal("1", Assert.IsType<SlotValue>(list.Items[0]).Text);
    }

    [Fact]
    public void A_value_with_no_socket_refuses_a_second_expression()
    {
        var form = Form(Formula.Binary("+", Formula.Number("1", 1d), Formula.Number("2", 2d)));
        form.SetCaret(form.SpanEnd);

        Assert.False(form.Place("3"));
        AssertSameShape(Formula.Binary("+", Formula.Number("1", 1d), Formula.Number("2", 2d)),
            Answer(form));

        Assert.True(form.Place(" * "));
        Assert.True(form.Place("0"));
        Assert.IsType<BinaryNode>(Answer(form));
    }

    [Fact]
    public void A_picked_token_takes_the_next_sign()
    {
        var form = Form(Formula.Binary("+", Formula.Number("1", 1d), Formula.Number("2", 2d)));
        var left = form.Recipe!.Walk().OfType<SlotValue>().First();

        form.Select(left);
        Assert.True(form.Place(" * "));
        Assert.True(form.Place("0"));

        var outer = Assert.IsType<BinaryNode>(Answer(form));
        Assert.Equal("+", outer.Operator);
        var inner = Assert.IsType<BinaryNode>(outer.Left);
        Assert.Equal("*", inner.Operator);
    }

    [Fact]
    public void Undo_puts_the_caret_back_where_the_edit_found_it()
    {
        var form = Form(Formula.Name("a"));
        form.SetCaret(form.SpanEnd);
        form.Place(" + ");
        var socket = form.Caret;
        form.Place("1");
        Assert.NotEqual(socket, form.Caret);

        form.Undo();

        Assert.Equal(socket, form.Caret);
    }


    [Fact]
    public void A_text_field_edits_one_body_and_has_no_band()
    {
        var form = new ValuePickerViewModel(Formula.Text("Hello"), Variables(), ActionValueForm.Text);
        Assert.True(form.IsTextMode);
        Assert.Equal("Hello", form.TextBody);
        Assert.Null(form.Recipe);
        Assert.True(form.CanConfirm);
        Assert.Equal(Formula.Text("Hello"), Answer(form));
    }

    [Fact]
    public void A_run_field_takes_text_but_no_signs()
    {
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(), ActionValueForm.Run);
        Assert.True(form.IsRunMode);
        Assert.False(form.ShowsFormulaMaterials);
        Assert.False(form.IsTypingNumber);

        form.TypingText = "C:\\out\\notes.txt";
        Assert.True(form.CanInsertTyped);
        form.PutTyped();

        var answer = Answer(form);
        Assert.Equal(Formula.Text("C:\\out\\notes.txt"), answer);
    }

    [Fact]
    public void A_name_field_can_create_a_name_the_step_writes()
    {
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(),
            ActionValueForm.Name, allowNewName: true);
        form.Wall.SearchText = "fresh";

        Assert.True(form.CanCreateName);
        ValuePick? picked = null;
        form.CloseRequested += value => picked = value;
        form.Confirm();

        Assert.NotNull(picked);
        Assert.Equal(new VariableNode("fresh"), picked!.Value);
    }


    [Fact]
    public void A_function_wraps_a_picked_value_as_its_first_argument()
    {
        var form = Form(Formula.Name("text"));
        var value = Assert.Single(form.Recipe!.Walk().OfType<SlotValue>());
        form.Select(value);

        form.PlaceCall("upper");

        var call = Assert.IsType<CallNode>(Answer(form));
        Assert.Equal("upper", call.Name);
        Assert.Equal(new VariableNode("text"), Assert.Single(call.Arguments));
    }


    [Fact]
    public void A_list_can_be_made_from_the_picked_value()
    {
        var form = Form(Formula.Name("text"));
        var value = Assert.Single(form.Recipe!.Walk().OfType<SlotValue>());
        form.Select(value);

        Assert.True(form.WrapList());

        var list = Assert.IsType<ListNode>(Answer(form));
        Assert.Equal(new VariableNode("text"), Assert.Single(list.Items));
        Assert.IsType<SlotList>(form.Recipe!.Walk().OfType<SlotList>().Single());
    }


    [Fact]
    public void A_piece_dropped_on_a_call_ghost_fills_that_argument()
    {
        var form = Form(Formula.Call("concat", Formula.Name("a"), Formula.Name("b")));
        var call = Assert.Single(form.Recipe!.Walk().OfType<SlotCall>());
        var first = call.Arguments[0].Items[0];

        Assert.True(form.MoveOnto(first, call.Ghost, after: false));

        var moved = Assert.IsType<CallNode>(Answer(form));
        Assert.Equal(2, moved.Arguments.Count);
        Assert.Equal(new VariableNode("b"), moved.Arguments[0]);
        Assert.Equal(new VariableNode("a"), moved.Arguments[1]);
    }


    [Fact]
    public void Not_makes_a_unary_token_instead_of_a_binary()
    {
        var form = Form(Formula.Name("flag"));

        Assert.True(form.Place("not"));

        Assert.IsType<UnaryNode>(Answer(form));
        Assert.IsType<SlotUnary>(Assert.Single(form.Recipe!.Walk().OfType<SlotUnary>()));
    }

    [Fact]
    public void The_root_of_a_whole_value_is_not_a_piece_that_can_be_dragged()
    {
        var form = Form(Formula.Binary("+", Formula.Name("a"), Formula.Number("1", 1d)));

        Assert.IsType<SlotRoot>(form.Recipe);
        Assert.False(form.Recipe!.CanRemove);
        Assert.All(form.Recipe.Walk().OfType<SlotSequence>(), run => Assert.False(run.CanRemove));
    }
}
