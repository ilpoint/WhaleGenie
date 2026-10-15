using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The structure page is the one a beginner is meant to reach for: the formula is a tree of lines,
/// a click wraps what is picked, and brackets are decided by the shape rather than typed. These
/// read back what the page would put in front of the user and what the tree comes to.
/// </summary>
public class ExpressionBuilderTreeTests
{
    private static IReadOnlyList<VariableChoice> Variables(params string[] names)
        => VariableChoicesForChecks.Named(names);

    private static ExpressionBuilderViewModel Builder(string expression, params string[] names)
        => new(expression, Variables(names));

    private static ExpressionFunctionChoice Function(ExpressionBuilderViewModel builder, string name)
        => builder.Functions.SelectMany(group => group.Functions).First(choice => choice.Name == name);

    [Fact]
    public void The_structure_page_lays_the_formula_out_one_line_to_a_piece()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$count + 1", "count");

            Assert.True(builder.IsTreePage);
            Assert.False(builder.TextUnreadable);
            Assert.Equal(new[] { "+", "$count", string.Empty },
                builder.Rows.Select(row => row.Token).ToArray());
            Assert.Equal(new[] { 0, 1, 1 }, builder.Rows.Select(row => row.Depth).ToArray());
            Assert.True(builder.Rows[2].IsTyped);
            Assert.Equal("1", builder.Rows[2].Literal);
            Assert.Equal(ExpressionItemKind.Number, builder.Rows[2].Kind);
            Assert.Equal("$count + 1", builder.Preview);
            Assert.True(builder.CanConfirm);
        });
    }

    [Fact]
    public void An_empty_field_opens_as_one_place_to_fill_in()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty);

            var only = Assert.Single(builder.Rows);
            Assert.True(only.IsHole);
            Assert.True(only.IsSelected);
            Assert.Equal(Strings.Get("ExprBuilder.Tree.Nothing"), builder.TreeMessage);

            // Nothing at all is not half a formula: closing the dialog on it clears the field, and
            // the page says so rather than holding the button back.
            Assert.True(builder.CanConfirm);
            string? handed = null;
            builder.CloseRequested += text => handed = text;
            builder.ConfirmCommand.Execute(null);
            Assert.Equal(string.Empty, handed);
        });
    }

    [Fact]
    public void Text_that_does_not_read_opens_where_it_can_be_typed()
    {
        Ui.Run(() =>
        {
            var builder = Builder("1 +");

            Assert.True(builder.IsTextPage);
            Assert.True(builder.TextUnreadable);

            // Asking for the structure anyway says what is wrong instead of showing a tree the user
            // did not build, and leaves the text exactly where it was.
            builder.ShowTreeCommand.Execute(null);
            Assert.True(builder.IsTreePage);
            Assert.True(builder.TextUnreadable);
            Assert.False(builder.CanConfirm);
            Assert.Equal("1 +", builder.Expression);

            // Nothing can be built on it either: the palette must not change a tree nobody can see.
            builder.ChooseVariable("count");
            Assert.DoesNotContain("$count", builder.Preview);
        });
    }

    [Fact]
    public void Picking_a_place_and_clicking_a_variable_fills_it()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "count");
            builder.ChooseVariable("count");

            Assert.Equal("$count", builder.Preview);
            Assert.Equal(ExpressionItemKind.Variable, Assert.Single(builder.Rows).Kind);
            Assert.True(builder.CanConfirm);
        });
    }

    [Fact]
    public void An_operator_wraps_what_is_picked_and_leaves_a_place_beside_it()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "a", "b");
            builder.ChooseVariable("a");
            builder.ChooseOperator(Operator(builder, "+"));

            // What was picked became the left operand, and the pick moved on to where the user
            // writes next: the place to fill in on the right.
            Assert.Equal("$a + ▢", builder.Preview);
            Assert.True(builder.Selected!.IsHole);
            Assert.False(builder.CanConfirm);

            builder.ChooseVariable("b");
            Assert.Equal("$a + $b", builder.Preview);
            Assert.True(builder.CanConfirm);
        });
    }

    [Fact]
    public void A_unary_operator_stands_in_front_of_what_is_picked()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "a");
            builder.ChooseVariable("a");
            builder.ChooseOperator(Operator(builder, "not"));

            Assert.Equal("not $a", builder.Preview);
            Assert.Equal(2, builder.Rows.Count);
        });
    }

    [Fact]
    public void A_function_holds_what_is_picked_as_its_first_argument()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "name");
            builder.ChooseVariable("name");
            builder.ChooseFunction(Function(builder, "upper"));

            Assert.Equal("upper($name)", builder.Preview);
            Assert.Equal("upper(text)", builder.Rows[0].Token);

            // Every argument says what it is called and what goes in it, which is what makes a
            // function something to read rather than something to memorise.
            var argument = builder.Rows[1];
            var parameter = Function(builder, "upper").Function.Parameters[0];
            Assert.Equal(ExpressionText.ParameterLabel(parameter), argument.Label);
            Assert.Contains(parameter.Name, argument.LabelHint);
        });
    }

    [Fact]
    public void A_function_with_more_than_one_argument_hands_the_next_one_the_pick()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "count");
            builder.ChooseVariable("count");
            builder.ChooseFunction(Function(builder, "if"));

            Assert.Equal("if($count, ▢, ▢)", builder.Preview);
            Assert.True(builder.Selected!.IsHole);
            Assert.Equal(2, builder.Rows.IndexOf(builder.Selected));
            Assert.False(builder.CanConfirm);
            Assert.Equal(Strings.Format("ExprBuilder.Tree.ToFill", 2), builder.TreeMessage);
        });
    }

    [Fact]
    public void A_function_that_takes_nothing_replaces_what_is_picked()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "count");
            builder.ChooseVariable("count");
            builder.ChooseFunction(Function(builder, "now"));

            Assert.Equal("now()", builder.Preview);
            Assert.True(builder.CanConfirm);
        });
    }

    [Fact]
    public void A_function_can_take_the_arguments_its_signature_marks_out()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty);
            builder.ChooseFunction(Function(builder, "substring"));

            Assert.Equal("substring(▢, ▢)", builder.Preview);
            Assert.True(builder.Rows[0].CanAddChild);

            builder.AddChild(builder.Rows[0]);
            Assert.Equal("substring(▢, ▢, ▢)", builder.Preview);

            // Three is as many as it takes, so there is nothing more to add.
            Assert.False(builder.Rows[0].CanAddChild);
            Assert.True(builder.Rows[0].CanDropChild);
        });
    }

    [Fact]
    public void A_list_takes_items_and_gives_them_back()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "a");
            builder.ChooseVariable("a");
            builder.ChooseOperator(Operator(builder, Strings.Get("ExprBuilder.Tree.List")));

            Assert.Equal("[$a]", builder.Preview);
            builder.AddChild(builder.Rows[0]);
            Assert.Equal("[$a, ▢]", builder.Preview);
            builder.DropChild(builder.Rows[0]);
            builder.DropChild(builder.Rows[0]);
            Assert.Equal("[]", builder.Preview);
        });
    }

    [Fact]
    public void Typing_in_a_place_settles_whether_it_is_a_number_or_piece_of_text()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty);
            var place = Assert.Single(builder.Rows);

            place.Literal = "12";
            Assert.Equal(ExpressionItemKind.Number, place.Kind);
            Assert.Equal("12", builder.Preview);

            place.Literal = "ada";
            Assert.Equal(ExpressionItemKind.Text, place.Kind);
            Assert.Equal("\"ada\"", builder.Preview);

            // Text that looks like a number is still text once the user says so.
            place.Literal = "5";
            place.ReadAsText = true;
            Assert.Equal("\"5\"", builder.Preview);
            place.ReadAsText = false;
            Assert.Equal("5", builder.Preview);

            // Emptying it again makes it a place to fill in, not an empty piece of text.
            place.Literal = string.Empty;
            Assert.True(place.IsHole);
        });
    }

    [Fact]
    public void The_page_says_how_many_places_are_still_to_fill_in()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + 1", "a");
            Assert.True(builder.CanConfirm);

            builder.Clear(builder.Rows[2]);

            Assert.Equal("$a + ▢", builder.Preview);
            Assert.False(builder.CanConfirm);
            Assert.Equal(Strings.Format("ExprBuilder.Tree.ToFill", 1), builder.TreeMessage);
            Assert.True(builder.ShowTreeNote);
        });
    }

    [Fact]
    public void Delete_takes_an_argument_away_while_the_function_can_do_without_it()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty);
            builder.ChooseFunction(Function(builder, "substring"));
            builder.AddChild(builder.Rows[0]);
            Assert.Equal(4, builder.Rows.Count);

            builder.Clear(builder.Rows[3]);
            Assert.Equal("substring(▢, ▢)", builder.Preview);

            // The two it cannot do without stay: that place simply empties again.
            builder.Clear(builder.Rows[1]);
            Assert.Equal("substring(▢, ▢)", builder.Preview);
            Assert.Equal(3, builder.Rows.Count);
        });
    }

    [Fact]
    public void The_two_pages_are_the_same_formula()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$count + 1", "count");
            builder.ShowTextCommand.Execute(null);
            Assert.True(builder.IsTextPage);
            Assert.Equal("$count + 1", builder.Expression);

            builder.Expression = "upper($count)";
            builder.ShowTreeCommand.Execute(null);

            Assert.True(builder.IsTreePage);
            Assert.False(builder.TextUnreadable);
            Assert.Equal("upper($count)", builder.Preview);
            Assert.Equal(2, builder.Rows.Count);
        });
    }

    [Fact]
    public void A_half_written_formula_is_not_handed_over_from_the_structure_page()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + 1", "a");
            builder.ShowTextCommand.Execute(null);
            builder.Expression = "1 +";
            builder.ShowTreeCommand.Execute(null);

            Assert.True(builder.TextUnreadable);
            Assert.False(builder.CanConfirm);

            // The text page keeps its own answer: it never held a formula back and still does not.
            builder.ShowTextCommand.Execute(null);
            Assert.True(builder.CanConfirm);
        });
    }

    [Fact]
    public void The_window_draws_a_line_for_every_piece_of_the_formula()
    {
        Ui.Run(() =>
        {
            var window = new ExpressionBuilderWindow(Builder("$count + 1", "count"));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var lines = window.FindControl<ItemsControl>("TreeRows");
            Assert.NotNull(lines);
            Assert.Equal(3, lines!.ItemCount);

            // And they are really on screen: a line with no size would be a template that binds
            // nothing anyone can read.
            var line = window.GetVisualDescendants().OfType<Border>()
                .First(control => control.Classes.Contains("Row"));
            Assert.True(line.Bounds.Width > 0);
            Assert.True(line.Bounds.Height > 0);
        });
    }

    /// <summary>
    /// Typing in a place happens while the lines are on screen, so the lines are not thrown away and
    /// built again as the user types: the box they are in the middle of would go with them.
    /// </summary>
    [Fact]
    public void Typing_in_a_place_leaves_the_box_it_is_typed_in_alone()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty);
            var window = new ExpressionBuilderWindow(builder);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var box = window.GetVisualDescendants().OfType<TextBox>()
                .First(control => control.Classes.Contains("Cell"));
            box.Text = "12";
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("12", builder.Preview);
            Assert.Contains(box, window.GetVisualDescendants());
        });
    }

    [Fact]
    public void A_variable_dragged_onto_a_piece_takes_its_place()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + 1", "a", "b");
            var place = builder.Rows[2];

            Assert.True(builder.CanLand(place, "var:b"));
            builder.Drop(place, "var:b");

            Assert.Equal("$a + $b", builder.Preview);
            Assert.Equal(ExpressionItemKind.Variable, builder.Rows[2].Kind);
            Assert.Same(builder.Rows[2], builder.Selected);
        });
    }

    [Fact]
    public void A_function_dragged_onto_a_piece_holds_it_as_its_first_argument()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + 1", "a");
            var payload = "fn:upper";
            Assert.True(builder.CanLand(builder.Rows[2], payload));

            builder.Drop(builder.Rows[2], payload);

            Assert.Equal("$a + upper(1)", builder.Preview);
            Assert.Equal("upper(text)", builder.Rows[2].Token);
        });
    }

    [Fact]
    public void A_piece_dragged_onto_a_place_moves_there_and_leaves_a_place_behind()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + $b", "a", "b");
            var payload = builder.PayloadOf(builder.Rows[2]);
            Assert.Equal("move:1", payload);

            // What is dragged travels as where it sits rather than as the piece itself, so the tree
            // is asked for it again by the same path when it lands.
            builder.Clear(builder.Rows[1]);
            Assert.Equal("▢ + $b", builder.Preview);

            var place = builder.Rows[1];
            Assert.True(builder.CanLand(place, payload));
            builder.Drop(place, payload);

            Assert.Equal("$b + ▢", builder.Preview);
            Assert.Same(builder.Rows[1], builder.Selected);
        });
    }

    [Fact]
    public void A_piece_dragged_into_its_own_inside_is_left_alone()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + upper($b)", "a", "b");
            var branch = builder.Rows[2];
            var payload = builder.PayloadOf(branch);
            Assert.Equal("move:1", payload);

            // A formula cannot hold itself, so the drag is turned down rather than folded in: the
            // line does not even light up.
            Assert.False(builder.CanLand(builder.Rows[3], payload));
            builder.Drop(builder.Rows[3], payload);

            Assert.Equal("$a + upper($b)", builder.Preview);
        });
    }

    [Fact]
    public void What_is_dragged_about_has_to_be_something_the_page_knows()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "a");
            var place = Assert.Single(builder.Rows);

            Assert.False(builder.CanLand(place, "nonsense"));
            Assert.False(builder.CanLand(place, "fn:nowhere"));
            Assert.False(builder.CanLand(place, "move:9.9"));
            Assert.False(builder.CanLand(null, "var:a"));
            Assert.False(builder.CanLand(place, null));

            // And a payload nothing can make sense of leaves the tree as it was rather than
            // quietly emptying the place it was dropped on.
            builder.Drop(place, "nonsense");
            Assert.Equal("▢", builder.Preview);
        });
    }

    /// <summary>
    /// The window is only the thin layer that hands a drop to the tree: it says which line the
    /// pointer is on and reads the short line that was dragged, both of which are the same on the
    /// way out and on the way in and would go wrong quietly if they drifted apart.
    /// </summary>
    [Fact]
    public void A_drop_on_a_line_is_read_back_the_way_it_was_dragged()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + 1", "a", "b");
            var window = new ExpressionBuilderWindow(builder);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var place = window.GetVisualDescendants().OfType<Border>()
                .First(border => border.DataContext is ExpressionItemViewModel { IsTyped: true });

            // What the palette and the tree put on a drag: the words under the mark of our own, and
            // the same words as plain text under it.
            var transfer = ExpressionDrag.Transfer("var:b");
            Assert.Equal("var:b", ExpressionDrag.Payload(transfer));
            Assert.Equal("var:b", transfer.TryGetText());
            Assert.Equal("var:b",
                transfer.TryGetValue(DataFormat.CreateStringApplicationFormat(ExpressionDrag.MarkName)));

            var over = Drag(place, DragDrop.DragOverEvent, transfer);
            Assert.Same(place.DataContext, builder.DropTarget);
            Assert.True(((ExpressionItemViewModel)place.DataContext!).IsDropTarget);
            Assert.Equal(DragDropEffects.Copy, over.DragEffects);

            Drag(place, DragDrop.DropEvent, transfer);
            Assert.Equal("$a + $b", builder.Preview);
            Assert.Null(builder.DropTarget);
        });
    }

    [Fact]
    public void A_drop_that_would_do_nothing_is_not_taken()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + 1", "a");
            var window = new ExpressionBuilderWindow(builder);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var place = window.GetVisualDescendants().OfType<Border>()
                .First(border => border.DataContext is ExpressionItemViewModel { IsTyped: true });

            var over = Drag(place, DragDrop.DragOverEvent, ExpressionDrag.Transfer("nonsense"));

            Assert.Equal(DragDropEffects.None, over.DragEffects);
            Assert.Null(builder.DropTarget);

            Drag(place, DragDrop.DropEvent, ExpressionDrag.Transfer("nonsense"));
            Assert.Equal("$a + 1", builder.Preview);
        });
    }

    /// <summary>Hands the window one of the drop events the platform would send, over a line.</summary>
    private static DragEventArgs Drag(Border line, RoutedEvent<DragEventArgs> what, DataTransfer transfer)
    {
        var args = new DragEventArgs(what, transfer, line, new Point(1, 1), KeyModifiers.None);
        line.RaiseEvent(args);
        return args;
    }

    /// <summary>
    /// What a press is on its way to drag. It is read off the entry the press went down on rather
    /// than off the line itself, because a button shows a word of its own and that word is what
    /// stands under the pointer, and because a press that is really a click — in a box, or on one of
    /// the little buttons a line carries — has to go on being that click.
    /// </summary>
    [Fact]
    public void What_a_press_would_drag_is_read_from_the_entry_it_went_down_on()
    {
        Ui.Run(() =>
        {
            var builder = Builder("substring($a, 1)", "a", "b");
            var window = new ExpressionBuilderWindow(builder);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var variable = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.DataContext is VariableChoice { Name: "b" });
            var function = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.DataContext is ExpressionFunctionChoice { Name: "upper" });

            // A piece of the tree says where it sits, counted from the root.
            Assert.Equal("move:0", window.DragPayload(Chip(window, ExpressionItemKind.Variable)));
            Assert.Equal("move:1", window.DragPayload(Chip(window, ExpressionItemKind.Number)));

            // A button says which variable or which function it stands for, whether the press went
            // down on the button or on the word shown inside it.
            Assert.Equal("var:b", window.DragPayload(variable));
            Assert.Equal("fn:upper", window.DragPayload(function));
            Assert.Equal("var:b", window.DragPayload(
                variable.GetVisualDescendants().OfType<TextBlock>().First()));

            // A box is the pointer going in to type, and the little buttons a line carries are
            // themselves: neither is anything to drag.
            var plus = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.DataContext is ExpressionItemViewModel
                    && button.Content as string == "+");
            Assert.Null(window.DragPayload(plus));
            Assert.Null(window.DragPayload(window));

            // And a place with nothing in it has nothing to drag either.
            builder.Clear(builder.Rows[1]);
            Dispatcher.UIThread.RunJobs();
            var box = window.GetVisualDescendants().OfType<TextBox>()
                .First(control => control.DataContext is ExpressionItemViewModel { IsHole: true });
            Assert.Null(window.DragPayload(box));
        });
    }

    /// <summary>The chip a piece of the formula is shown as, found by what the piece stands for.</summary>
    private static Border Chip(Window window, ExpressionItemKind kind)
        => window.GetVisualDescendants().OfType<Border>()
            .First(border => border.Classes.Contains("Chip")
                && border.DataContext is ExpressionItemViewModel piece && piece.Kind == kind);

    private static TreeOperator Operator(ExpressionBuilderViewModel builder, string token)
        => builder.TreeOperators.First(item => item.Token == token);
}
