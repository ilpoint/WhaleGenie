using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The one window that fills a cell. The band is a run of pieces — values, signs, brackets, and calls
/// that hold further runs — so the model is judged on the things that run has to promise: everything
/// written is a piece you can see and take away, a call keeps the places it still asks for, and what
/// is handed back is exactly the text the pieces add up to.
/// </summary>
public class ValuePickerTests
{
    private static IReadOnlyList<VariableChoice> Variables() =>
    [
        new VariableChoice { Name = "count", Scope = VariableScope.Local, Type = "number" },
        new VariableChoice { Name = "title", Scope = VariableScope.Local, Type = "text" },
    ];

    private static SlotSequence Root(ValuePickerViewModel form) => Assert.IsType<SlotSequence>(form.Recipe);

    [Fact]
    public void An_empty_formula_is_one_place_to_start_from()
    {
        var form = new ValuePickerViewModel(null, Variables(), namesOnly: false);

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
        var form = new ValuePickerViewModel("$a + 1", Variables(), namesOnly: false);

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
        var form = new ValuePickerViewModel("concat($a, $b)", Variables(), namesOnly: false);

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
        var form = new ValuePickerViewModel("concat($a, upper($b))", Variables(), namesOnly: false);

        var outer = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        var inner = Assert.IsType<SlotCall>(Assert.Single(outer.Arguments[1].Items));

        Assert.Equal("upper", inner.Name);
        Assert.Equal("$b", Assert.IsType<SlotValue>(Assert.Single(inner.Arguments[0].Items)).Text);
    }

    /// <summary>An argument written empty is a place, red dashed, waiting to be filled.</summary>
    [Fact]
    public void An_argument_written_empty_is_a_place_waiting()
    {
        var form = new ValuePickerViewModel("contains($title, )", Variables(), namesOnly: false);

        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Equal(2, call.Arguments.Count);
        var hole = Assert.IsType<SlotEmpty>(Assert.Single(call.Arguments[1].Items));
        Assert.True(hole.Required);
    }

    /// <summary>A call with nothing in it says so, and still offers the place to fill.</summary>
    [Fact]
    public void A_call_with_nothing_in_it_says_so_and_still_offers_the_place_to_fill()
    {
        var form = new ValuePickerViewModel("upper()", Variables(), namesOnly: false);

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
        var form = new ValuePickerViewModel("123 (", Variables(), namesOnly: false);

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
        var form = new ValuePickerViewModel("$a ? $b", Variables(), namesOnly: false);

        var items = Root(form).Items;

        Assert.Equal(3, items.Count);
        Assert.Equal("?", Assert.IsType<SlotOperator>(items[1]).Text);
    }

    /// <summary>Taking a piece away takes it and the comma that kept it from its neighbour.</summary>
    [Fact]
    public void Taking_a_piece_away_takes_its_comma_too()
    {
        var form = new ValuePickerViewModel("concat($a, $b, $c)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(form.RemoveArgument(call.Arguments[1].Items[0]));

        Assert.Equal("concat($a, $c)", form.Text);
    }

    /// <summary>Taking the first piece away takes the comma behind it instead.</summary>
    [Fact]
    public void Taking_the_first_piece_away_keeps_the_rest_whole()
    {
        var form = new ValuePickerViewModel("concat($a, $b)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(form.RemoveArgument(call.Arguments[0].Items[0]));

        Assert.Equal("concat($b)", form.Text);
    }

    /// <summary>A sign goes on its own; what is left may not read, and the line under says so.</summary>
    [Fact]
    public void A_sign_can_be_taken_away_on_its_own()
    {
        var form = new ValuePickerViewModel("$a and $b", Variables(), namesOnly: false);

        Assert.True(form.RemoveArgument(Root(form).Items[1]));

        Assert.Equal("$a $b", form.Text);
    }

    /// <summary>
    /// A function card adds an empty call of its own. It never puts itself around a piece already
    /// written: taking over what the person had built was the reason for removing wrapping.
    /// </summary>
    [Fact]
    public void A_function_card_does_not_take_over_a_piece()
    {
        var form = new ValuePickerViewModel("$title", Variables(), namesOnly: false);

        form.PlaceCall("upper");

        Assert.Equal("$title upper()", form.Text);
    }

    /// <summary>A piece added to the line goes on its end, leaving what is written alone.</summary>
    [Fact]
    public void A_piece_goes_on_the_end_of_the_line()
    {
        var form = new ValuePickerViewModel("$a +", Variables(), namesOnly: false);

        form.Place("1");

        Assert.Equal("$a + 1", form.Text);
    }

    /// <summary>Dragging one argument onto another puts it there, with the commas put right.</summary>
    [Fact]
    public void A_piece_dropped_on_the_first_argument_goes_in_front_of_it()
    {
        var form = new ValuePickerViewModel("concat($a, $b, $c)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(form.MoveOnto(call.Arguments[2].Items[0], call.Arguments[0].Items[0], after: false));

        Assert.Equal("concat($c, $a, $b)", form.Text);
    }

    /// <summary>A piece can be dragged from one call into another.</summary>
    [Fact]
    public void A_piece_can_be_dragged_from_one_call_into_another()
    {
        var form = new ValuePickerViewModel("concat($a, $b) and join($c)", Variables(), namesOnly: false);
        var calls = Root(form).Items.OfType<SlotCall>().ToList();

        Assert.True(form.MoveOnto(calls[1].Arguments[0].Items[0], calls[0].Arguments[1].Items[0], false));

        Assert.Equal("concat($a, $c, $b) and join()", form.Text);
    }

    /// <summary>Undo goes back a step at a time, and redo returns to where it was.</summary>
    [Fact]
    public void Undo_and_redo_walk_the_line()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);

        form.Text = "$a and $b";
        form.Text = "$a and $b and $c";

        Assert.True(form.CanUndo);

        form.Undo();
        Assert.Equal("$a and $b", form.Text);

        form.Undo();
        Assert.Equal("$a", form.Text);
        Assert.False(form.CanUndo);

        form.Redo();
        Assert.Equal("$a and $b", form.Text);
        Assert.True(form.CanRedo);
    }

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

    /// <summary>A seam is measured before the cut and has to move by whatever the cut actually took.</summary>
    [Fact]
    public void A_seam_before_a_cut_moves_by_what_the_cut_took()
    {
        var span = new ValuePickerViewModel.CutSpan(3, 6);

        Assert.Equal(2, ValuePickerViewModel.SeamAfterCut(2, span));
        Assert.Equal(3, ValuePickerViewModel.SeamAfterCut(4, span));
        Assert.Equal(3, ValuePickerViewModel.SeamAfterCut(6, span));
        Assert.Equal(4, ValuePickerViewModel.SeamAfterCut(7, span));
    }

    /// <summary>Taking a sign out from between two numbers must not weld them into one.</summary>
    [Fact]
    public void Taking_a_sign_out_from_between_two_numbers_does_not_weld_them()
    {
        var form = new ValuePickerViewModel("1-2", Variables(), namesOnly: false);

        Assert.True(form.RemoveArgument(Root(form).Items[1]));

        Assert.Equal("1 2", form.Text);
    }

    /// <summary>A cross belongs to the piece the pointer is on, not to everything it sits inside.</summary>
    [Fact]
    public void A_cross_shows_only_where_the_pointer_is()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        var value = Root(form).Items[0];

        Assert.True(value.CanRemove);
        Assert.False(value.ShowsRemove);

        value.IsHovered = true;
        Assert.True(value.ShowsRemove);

        value.IsHovered = false;
        Assert.False(value.ShowsRemove);
    }

    /// <summary>
    /// The band draws its cursor from the preview and applies the same thing on release, so the two
    /// cannot disagree — and a spot the piece already fills is reported as "nothing would happen".
    /// </summary>
    [Fact]
    public void A_preview_says_where_the_piece_would_land()
    {
        var form = new ValuePickerViewModel("concat($a, $b)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        var onto = form.PreviewMove(call.Arguments[1].Items[0], call.Arguments[0].Items[0], after: false);
        Assert.True(onto.Allowed);
        Assert.False(onto.NoOp);
        Assert.Same(call.Arguments[0].Items[0], onto.Anchor);

        var same = form.PreviewMove(call.Arguments[1].Items[0], call.Arguments[0].Items[0], after: true);
        Assert.True(same.NoOp);
        Assert.False(same.Allowed);
    }

    /// <summary>
    /// A drop into the space of a run belongs after its last piece. The cursor has to be drawn on
    /// that piece, not on the whole run the pointer happened to land in.
    /// </summary>
    [Fact]
    public void A_drop_in_a_run_points_at_its_last_piece()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        var root = Root(form);
        var last = root.Items.Last(one => one.Length > 0);

        var preview = form.PreviewMove(root.Items[0], root, after: false);

        Assert.Same(last, preview.Anchor);
        Assert.True(preview.After);
    }

    /// <summary>A run that folds reads top to bottom; a single piece still reads left to right.</summary>
    [Fact]
    public void A_folded_run_reads_top_to_bottom()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        var root = Root(form);
        var value = root.Items[0];

        Assert.True(ValuePickerViewModel.AfterFor(root, rightHalf: false, lowerHalf: true));
        Assert.False(ValuePickerViewModel.AfterFor(root, rightHalf: true, lowerHalf: false));
        Assert.True(ValuePickerViewModel.AfterFor(value, rightHalf: true, lowerHalf: false));
        Assert.False(ValuePickerViewModel.AfterFor(value, rightHalf: false, lowerHalf: true));
    }

    /// <summary>Written arguments say what they are for, not just the empty ones.</summary>
    [Fact]
    public void Arguments_carry_a_name_a_position_and_whether_they_are_optional()
    {
        var form = new ValuePickerViewModel("substring($a, 1)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.Equal(2, call.Arguments.Count);
        Assert.Equal(1, call.Arguments[0].ArgumentIndex);
        Assert.Equal(2, call.Arguments[1].ArgumentIndex);
        Assert.True(call.Arguments[0].HasArgumentLabel);
        Assert.True(call.Arguments[0].HasArgumentFlag);
        Assert.False(call.Arguments[0].ArgumentOptional);
    }

    /// <summary>A missing required argument is pointed at the place that is still empty.</summary>
    [Fact]
    public void A_missing_required_argument_is_marked_on_the_place_it_belongs_to()
    {
        var form = new ValuePickerViewModel("substring($a)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        var ghost = Assert.IsType<SlotEmpty>(call.Ghost);

        Assert.True(ghost.HasProblem);
        Assert.NotEmpty(ghost.ProblemHint);
        Assert.Equal(ghost.ProblemHint, ghost.HoverHint);
        Assert.False(form.CanConfirm);
        Assert.True(form.HasBlockReason);
    }

    /// <summary>An unknown function is marked on its call, not on the whole formula.</summary>
    [Fact]
    public void An_unknown_function_is_marked_on_its_call()
    {
        var form = new ValuePickerViewModel("nope($a)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        Assert.True(call.HasProblem);
        Assert.NotEmpty(call.ProblemHint);
        Assert.False(Root(form).HasProblem);
    }

    /// <summary>Writing a readable formula clears every mark and the reason the button was grey.</summary>
    [Fact]
    public void A_good_formula_clears_the_marks_and_the_reason()
    {
        var form = new ValuePickerViewModel("nope($a)", Variables(), namesOnly: false);
        Assert.Contains(form.Recipe!.Walk(), one => one.HasProblem);

        form.Text = "upper($a)";

        Assert.DoesNotContain(form.Recipe!.Walk(), one => one.HasProblem);
        Assert.True(form.CanConfirm);
        Assert.False(form.HasBlockReason);
    }

    private static SlotCall OnlyCall(ValuePickerViewModel form)
        => Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

    /// <summary>More than three arguments get one row each; a short call stays on one line.</summary>
    [Fact]
    public void SlotStackingExpandsForManyArguments()
    {
        var many = OnlyCall(new ValuePickerViewModel("concat($a, $b, $c, $d)", Variables(), false));
        var few = OnlyCall(new ValuePickerViewModel("concat($a, $b)", Variables(), false));

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
        var call = OnlyCall(new ValuePickerViewModel("get(list(1, 2), 2)", Variables(), false));
        Assert.False(call.Stacked);
        Assert.True(call.Compact);
    }

    /// <summary>A short formula, and a formula with no call at all, keep the compact row.</summary>
    [Fact]
    public void SlotStackingKeepsShortFormulaOnOneLine()
    {
        Assert.False(OnlyCall(new ValuePickerViewModel("upper($a)", Variables(), false)).Stacked);
        var plain = new ValuePickerViewModel("1 + 2", Variables(), false);
        Assert.Empty(Root(plain).Items.OfType<SlotCall>());
    }

    /// <summary>The same call folds when the band is narrow and opens out when it is wide.</summary>
    [Fact]
    public void SlotStackingUsesAvailableWidth()
    {
        // Long enough that the smaller per-character estimate still crosses a 200 pixel band.
        var form = new ValuePickerViewModel("concat($alpha, $beta)", Variables(), false);
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
        var form = new ValuePickerViewModel("concat(upper($a), lower($b), 1, 2)", Variables(), false);
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
        var call = OnlyCall(new ValuePickerViewModel("concat($a, $b, $c, $d)", Variables(), false));
        Assert.True(call.Stacked);

        Assert.True(ValuePickerViewModel.AfterFor(call, rightHalf: false, lowerHalf: true));
        Assert.False(ValuePickerViewModel.AfterFor(call, rightHalf: true, lowerHalf: false));
    }

    /// <summary>The insertion point starts where a formula being built a piece at a time goes on.</summary>
    [Fact]
    public void The_caret_starts_at_the_end_of_the_formula()
    {
        var form = new ValuePickerViewModel("$a + 1", Variables(), namesOnly: false);

        Assert.Equal(form.Text.Length, form.Caret);
    }

    [Fact]
    public void The_caret_is_clamped_to_the_formula()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);

        form.SetCaret(-20);
        Assert.Equal(0, form.Caret);

        form.SetCaret(500);
        Assert.Equal(form.Text.Length, form.Caret);
    }

    /// <summary>Reading a shorter line again must not leave the line pointing past its end.</summary>
    [Fact]
    public void A_shorter_formula_pulls_the_caret_back_with_it()
    {
        var form = new ValuePickerViewModel("$a + $b + $c", Variables(), namesOnly: false);
        form.SetCaret(form.Text.Length);

        form.Text = "$a";

        Assert.Equal(form.Text.Length, form.Caret);
    }

    /// <summary>Writing in the middle adds a piece; it never takes over the pieces on either side.</summary>
    [Fact]
    public void A_piece_written_at_the_caret_leaves_the_rest_alone()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        form.SetCaret(2);

        form.Place("$c");

        Assert.Equal("$a $c + $b", form.Text);
        Assert.Equal(5, form.Caret);
    }

    [Fact]
    public void Two_word_pieces_meeting_at_the_caret_get_a_space()
    {
        var form = new ValuePickerViewModel("count", Variables(), namesOnly: false);
        form.SetCaret(0);

        form.Place("$a");

        Assert.Equal("$a count", form.Text);
    }

    /// <summary>A function card leaves the line between its brackets, ready for the first argument.</summary>
    [Fact]
    public void A_function_card_leaves_the_caret_between_its_brackets()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);

        form.PlaceCall("upper");

        Assert.Equal("upper()", form.Text);
        Assert.Equal("upper(".Length, form.Caret);
    }

    [Fact]
    public void The_next_piece_goes_inside_a_call_the_card_just_wrote()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);
        form.PlaceCall("upper");

        form.Place("$a");

        Assert.Equal("upper($a)", form.Text);
    }

    /// <summary>The ghost is still the way to add an argument now that the plus button is gone.</summary>
    [Fact]
    public void Filling_the_ghost_writes_its_comma_and_grows_another_one()
    {
        var form = new ValuePickerViewModel("concat($a)", Variables(), namesOnly: false);
        var call = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));

        form.SetCaret(call.Ghost!.Start);
        form.Place("$b");

        Assert.Equal("concat($a, $b)", form.Text);
        var after = Assert.IsType<SlotCall>(Assert.Single(Root(form).Items));
        Assert.Equal(2, after.Arguments.Count);
        Assert.NotNull(after.Ghost);
    }

    /// <summary>A bracket from the strip writes one character and nothing is put inside a pair.</summary>
    [Fact]
    public void A_single_bracket_is_written_as_one_character()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);
        form.SetCaret(0);

        form.Place("(");

        Assert.Equal("($a", form.Text);
    }

    /// <summary>The band is rebuilt after every edit; the insertion point is a character offset, so it stays.</summary>
    [Fact]
    public void The_caret_survives_a_rebuilt_band()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        form.SetCaret(2);

        form.Text = "$a + $b + $c";

        Assert.Equal(2, form.Caret);
    }

    [Fact]
    public void A_piece_put_before_a_name_does_not_take_it_over()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        form.SetCaret(5);

        form.Place("$c");

        Assert.Equal("$a + $c $b", form.Text);
    }

    /// <summary>A call added at the front must not fuse with the name that follows it.</summary>
    [Fact]
    public void A_call_put_at_the_front_does_not_fuse_with_the_next_piece()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), namesOnly: false);
        form.SetCaret(0);

        form.PlaceCall("upper");

        Assert.Equal("upper() $a + $b", form.Text);
    }

    /// <summary>The standing field opens ready for a number, the more common thing to type.</summary>
    [Fact]
    public void The_small_field_opens_in_number_mode()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);

        Assert.True(form.IsTypingNumber);
        Assert.False(form.IsTypingText);
    }

    /// <summary>A number and a piece of text are written differently, so switching forgets the half-typed one.</summary>
    [Fact]
    public void Switching_the_field_mode_clears_what_was_typed()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);
        form.TypingText = "12.5";

        form.SetTypingMode(number: false);

        Assert.True(form.IsTypingText);
        Assert.Equal(string.Empty, form.TypingText);
        form.TypingText = "hello";
        Assert.Equal("\"hello\"", form.TypedToken());
    }

    [Fact]
    public void Clearing_the_field_leaves_it_ready_for_the_next_value()
    {
        var form = new ValuePickerViewModel("$a", Variables(), namesOnly: false);
        form.TypingText = "42";

        form.ClearTyped();

        Assert.Equal(string.Empty, form.TypingText);
        Assert.False(form.HasTypingNote);
        Assert.False(form.CanInsertTyped);
    }

    /// <summary>A space already standing at the seam is the separator; a second one would leave a gap.</summary>
    [Fact]
    public void A_space_already_at_the_seam_is_not_doubled()
    {
        var form = new ValuePickerViewModel("42 $a", Variables(), namesOnly: false);
        form.SetCaret(2);

        form.Place("\"hello\"");

        Assert.Equal("42 \"hello\" $a", form.Text);
    }
}
