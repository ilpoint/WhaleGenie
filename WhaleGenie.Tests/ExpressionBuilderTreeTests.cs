using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Expressions;
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

    /// <summary>
    /// An empty field with its one place picked, which is what a click from the palette needs: the
    /// page never picks a place on its own.
    /// </summary>
    private static ExpressionBuilderViewModel Empty(params string[] names)
    {
        var builder = Builder(string.Empty, names);
        builder.Select(builder.Rows[0]);
        return builder;
    }

    private static ExpressionFunctionChoice Function(ExpressionBuilderViewModel builder, string name)
        => builder.Functions.First(choice => choice.Name == name);

    /// <summary>
    /// Drops a card on the place that is picked, which is how the page is used: a piece is placed by
    /// being let go over the line it belongs to. The first drop on an empty formula lands on its one
    /// line, which is also the only place there is.
    /// </summary>
    private static void Land(ExpressionBuilderViewModel builder, string payload)
        => builder.Drop(builder.Selected ?? builder.Rows[0], payload);


    /// <summary>
    /// A click on a card places nothing: a piece goes where it is let go, and a click has no such
    /// place. The page answers with what to do instead, and the formula is exactly as it was —
    /// including the case that used to replace the whole formula when nothing was picked.
    /// </summary>
    [Fact]
    public void A_click_on_a_card_places_nothing()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$count + 1", "count", "other");

            builder.SayDrag();
            Assert.Equal("$count + 1", builder.Preview);
            Assert.True(builder.HasPaletteNotice);
            Assert.Equal(Strings.Get("ExprBuilder.DragToPlace"), builder.PaletteNotice);

            // A drop is the thing that works, and it takes the notice away with it. It lands on the
            // line it was let go over, which here is the left operand.
            builder.Drop(builder.Rows[1], "var:other");
            Assert.Equal("$other + 1", builder.Preview);
            Assert.False(builder.HasPaletteNotice);
        });
    }

    /// <summary>
    /// An empty formula is one place and no more, and a drop is what fills it: the click that used to
    /// fall through to that one place leaves it empty.
    /// </summary>
    [Fact]
    public void An_empty_field_is_filled_by_a_drop_and_not_by_a_click()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty, "count");

            builder.SayDrag();
            Assert.Equal("▢", builder.Preview);
            Assert.True(builder.HasPaletteNotice);

            builder.Drop(builder.Rows[0], "var:count");
            Assert.Equal("$count", builder.Preview);
            Assert.False(builder.HasPaletteNotice);
        });
    }

    /// <summary>
    /// The window is split into the two walls the page is built from and the page itself: names,
    /// values and operators along the bottom of the tree they are dragged onto, functions along the
    /// bottom of the right column, and what the page has to say at the top right.
    /// </summary>
    [Fact]
    public void The_cards_sit_along_the_bottom_of_the_page_and_the_notes_at_the_top_right()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$count + 1", "count");
            var window = new ExpressionBuilderWindow(builder);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var tree = window.FindControl<ItemsControl>("TreeRows")!;
            var build = window.FindControl<Border>("BuildPanel")!;
            var hint = window.FindControl<StackPanel>("HintBlock")!;
            var formula = window.FindControl<Border>("FormulaPanel")!;
            var functions = window.FindControl<Border>("FunctionPanel")!;

            var treeBottom = tree.TranslatePoint(new Point(0, 0), window)!.Value.Y + tree.Bounds.Height;
            var buildTop = build.TranslatePoint(new Point(0, 0), window)!.Value.Y;
            Assert.True(buildTop >= treeBottom - 0.5);

            // The two walls are one on each side, both under the page.
            var buildLeft = build.TranslatePoint(new Point(0, 0), window)!.Value.X;
            var functionLeft = functions.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.True(functionLeft > buildLeft + build.Bounds.Width);

            // What the page has to say is above the function wall, and the hint above that.
            Assert.True(hint.Bounds.Y < formula.Bounds.Y);
            Assert.True(formula.Bounds.Y < functions.Bounds.Y);

            window.Close();
        });
    }

    /// <summary>
    /// The card keeps its own shape: it says the function's name and nothing else, and how many
    /// places the function asks for is left to the bubble over it.
    /// </summary>
    [Fact]
    public void A_card_shows_its_name_and_leaves_the_rest_to_the_bubble()
    {
        Ui.Run(() =>
        {
            var builder = Builder("1");
            var window = new ExpressionBuilderWindow(builder);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var card = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.DataContext is ExpressionFunctionChoice { Name: "abs" });
            var choice = (ExpressionFunctionChoice)card.DataContext!;

            var written = card.GetVisualDescendants().OfType<TextBlock>()
                .Select(text => text.Text ?? string.Empty)
                .ToList();
            Assert.Equal([choice.Heading], written);

            Assert.NotNull(ToolTip.GetTip(card));

            window.Close();
        });
    }

    /// <summary>
    /// A click on a function card needs a place picked on purpose: on a formula that is already there
    /// nothing is picked when the page opens, so the click says what to do instead of wrapping
    /// whatever happens to be at the top.
    /// </summary>
    /// <summary>
    /// A card says how many places the function asks for, so a function that wants three arguments and
    /// allows three more says so before it is dropped into a formula.
    /// </summary>
    [Fact]
    public void A_card_says_how_many_places_the_function_asks_for()
    {
        Ui.Run(() =>
        {
            var builder = Builder(string.Empty);
            Assert.Equal(Strings.Format("ExprBuilder.ArityFixed", 1),
                Function(builder, "abs").ArityNote);
            Assert.Equal(Strings.Format("ExprBuilder.ArityRange", 3, 3),
                Function(builder, "compare").ArityNote);

            var open = builder.Functions.First(choice => choice.Function.MaxArguments == int.MaxValue);
            Assert.Equal(Strings.Format("ExprBuilder.ArityAtLeast", open.Function.MinArguments),
                open.ArityNote);
        });
    }

    /// <summary>
    /// The page written out by hand is a buffer: what is typed there changes nothing until it is
    /// built, so a formula dragged together cannot be lost to a stray keystroke.
    /// </summary>
    [Fact]
    public void The_handwritten_page_changes_nothing_until_it_is_built()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$count", "count");
            builder.ShowTextCommand.Execute(null);
            Assert.True(builder.IsTextPage);
            Assert.Equal("$count", builder.Written);

            builder.Written = "1 +";
            Assert.False(builder.CanConfirm);

            builder.Written = "$count + 1";
            Assert.Equal("$count", builder.Preview);
            Assert.True(builder.HasUnappliedWritten);
            Assert.False(builder.CanConfirm);

            builder.RebuildCommand.Execute(null);
            Assert.True(builder.IsTreePage);
            Assert.Equal("$count + 1", builder.Preview);
            Assert.False(builder.HasUnappliedWritten);
            Assert.True(builder.CanConfirm);
        });
    }

    /// <summary>
    /// A function's arguments are not six numbers to type: the comparison is one of its own words and
    /// the flags are ticks, which is what makes compare() readable as the sentence it is.
    /// </summary>
    [Fact]
    public void A_comparison_argument_is_picked_and_a_yes_or_no_is_ticked()
    {
        Ui.Run(() =>
        {
            var compare = WhaleGenie.Core.Expressions.Expression.Functions
                .First(function => function.Name == "compare");
            Assert.Equal(WhaleGenie.Core.Expressions.ExpressionParameterKind.Choice, compare.Parameters[1].Kind);
            Assert.Contains("contains", compare.Parameters[1].Choices);
            Assert.Equal(WhaleGenie.Core.Expressions.ExpressionParameterKind.Flag, compare.Parameters[3].Kind);

            var builder = Builder("compare($a, \"contains\", \"x\", true)", "a");
            var slots = builder.Rows.Skip(1).ToList();

            // The first argument is a variable that was already there, so it stays what it is.
            Assert.False(slots[0].IsTyped);
            Assert.True(slots[1].IsChoice);
            Assert.Contains(slots[1].Choices, word => word.Word == "contains");
            Assert.False(slots[1].IsTextBox);
            Assert.True(slots[3].IsFlag);
            Assert.True(slots[3].FlagValue);

            slots[1].ChosenWord = slots[1].Choices.First(word => word.Word == "regex");
            slots[3].FlagValue = false;

            Assert.Contains("regex", builder.Preview, System.StringComparison.Ordinal);
            Assert.Contains("false", builder.Preview, System.StringComparison.Ordinal);
            Assert.DoesNotContain("\"true\"", builder.Preview, System.StringComparison.Ordinal);
        });
    }

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

            // Nothing is picked: a click from the palette needs a place chosen on purpose, and
            // picking this one for the user is what used to make a click insert by itself.
            Assert.False(only.IsSelected);
            Assert.Null(builder.Selected);
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
            Land(builder, "var:count");
            Assert.DoesNotContain("$count", builder.Preview);
        });
    }

    [Fact]
    public void A_variable_dropped_on_the_picked_place_fills_it()
    {
        Ui.Run(() =>
        {
            var builder = Empty("count");
            Land(builder, "var:count");

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
            var builder = Empty("a", "b");
            Land(builder, "var:a");
            Land(builder, "op:" + Operator(builder, "+").Token);

            // What was picked became the left operand, and the pick moved on to where the user
            // writes next: the place to fill in on the right.
            Assert.Equal("$a + ▢", builder.Preview);
            Assert.True(builder.Selected!.IsHole);
            Assert.False(builder.CanConfirm);

            Land(builder, "var:b");
            Assert.Equal("$a + $b", builder.Preview);
            Assert.True(builder.CanConfirm);
        });
    }

    [Fact]
    public void A_unary_operator_stands_in_front_of_what_is_picked()
    {
        Ui.Run(() =>
        {
            var builder = Empty("a");
            Land(builder, "var:a");
            Land(builder, "op:" + Operator(builder, "not").Token);

            Assert.Equal("not $a", builder.Preview);
            Assert.Equal(2, builder.Rows.Count);
        });
    }

    [Fact]
    public void A_function_holds_what_is_picked_as_its_first_argument()
    {
        Ui.Run(() =>
        {
            var builder = Empty("name");
            Land(builder, "var:name");
            Land(builder, "fn:upper");

            Assert.Equal("upper($name)", builder.Preview);
            Assert.Equal(ExpressionText.Heading(Function(builder, "upper").Function),
                builder.Rows[0].Token);

            // Every argument says what it is called and what goes in it, which is what makes a
            // function something to read rather than something to memorise.
            var upper = Function(builder, "upper");
            var argument = builder.Rows[1];
            var parameter = upper.Function.Parameters[0];
            Assert.Equal("[" + ExpressionText.ParameterLabel(upper.Name, parameter) + "]",
                argument.Label);
            Assert.Contains(parameter.Name, argument.LabelHint);
        });
    }

    /// <summary>
    /// A line standing for a call says which function it is the same way the button that put it
    /// there did: the name in the reader's own language, with the name the code calls it by beside
    /// it, so a formula read back on the structure page is not a row of unfamiliar words.
    /// </summary>
    [Fact]
    public void A_call_line_names_the_function_in_both_languages()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var builder = Builder("upper($a)", "a");

            Assert.Equal("转大写 (upper)", builder.Rows[0].Token);
        }));
    }

    /// <summary>
    /// A leaf standing for one of WhaleGenie's own variables reads the way the wall's button does:
    /// the Chinese name with the code name beside it, so a formula made by pointing at buttons can
    /// be read back afterwards without knowing $sys.clipboard by heart.
    /// </summary>
    [Fact]
    public void A_variable_leaf_names_what_whalegenie_provides_in_both_languages()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var builder = Builder("$sys.clipboard");

            Assert.Equal("剪贴板 ($sys.clipboard)", builder.Rows[0].Token);
            Assert.Equal("$sys.clipboard", builder.Preview);
        }));
    }

    /// <summary>
    /// Under the formula the structure page says what the formula is doing in plain Chinese, which is
    /// the line somebody who does not follow a formula checks their own logic against before running
    /// anything. It is said about the tree rather than about the text written out, so a place still to
    /// fill in has a way of being said and the sentence grows along with the formula.
    /// </summary>
    [Fact]
    public void The_structure_page_says_what_the_formula_is_doing_in_plain_chinese()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var header = Strings.Get("Expr.Say.Header");
            var builder = Empty("count");

            // Nothing written is nothing to read out, and neither is a place to fill in on its own.
            Assert.Equal(string.Empty, builder.Saying);
            Assert.False(builder.ShowsSaying);

            Land(builder, "var:count");
            Land(builder, "op:" + Operator(builder, "+").Token);
            Assert.Equal(header + "count加上待填", builder.Saying);
            Assert.True(builder.ShowsSaying);

            builder.Rows[2].Literal = "1";
            Assert.Equal(header + "count加上1", builder.Saying);

            // A variable WhaleGenie provides is said by its Chinese name, which is what a reader who
            // has never met $sys.clipboard recognises.
            Assert.Equal(header + "剪贴板", Builder("$sys.clipboard").Saying);
        }));
    }

    /// <summary>
    /// The sentence belongs to the page the formula is read on: the page that writes it out by hand
    /// has the formula itself, and a reading of one that does not read is a reading of nothing.
    /// </summary>
    [Fact]
    public void The_sentence_is_shown_on_the_structure_page_and_nowhere_it_has_nothing_to_say()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var builder = Builder("$count + 1", "count");
            Assert.True(builder.ShowsSaying);

            builder.ShowTextCommand.Execute(null);
            Assert.False(builder.ShowsSaying);

            builder.ShowTreeCommand.Execute(null);
            Assert.True(builder.ShowsSaying);

            var unreadable = Builder("1 +");
            Assert.Equal(string.Empty, unreadable.Saying);
            Assert.False(unreadable.ShowsSaying);
            unreadable.ShowTreeCommand.Execute(null);
            Assert.Equal(string.Empty, unreadable.Saying);
        }));
    }

    /// <summary>
    /// The sentence is written in Chinese and there is no English one to write, so an English
    /// interface leaves the line out rather than showing a key or a half-read sentence.
    /// </summary>
    [Fact]
    public void An_english_interface_has_no_sentence_to_read()
    {
        Ui.Run(() => InterfaceLanguage.InEnglish(() =>
        {
            var builder = Builder("$count + 1", "count");

            Assert.Equal(string.Empty, builder.Saying);
            Assert.False(builder.ShowsSaying);
        }));
    }

    /// <summary>
    /// The sentence is really on the page rather than only in the view model: a binding that names
    /// nothing is a line that quietly never shows, and nobody would report a line that is missing.
    /// </summary>
    [Fact]
    public void The_window_shows_the_sentence_under_the_formula()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var builder = Builder("$count + 1", "count");
            var window = new ExpressionBuilderWindow(builder);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var line = window.FindControl<TextBlock>("SayingLine");
            Assert.NotNull(line);
            Assert.True(line!.IsVisible);
            Assert.Equal(builder.Saying, line.Text);
            Assert.Contains("count加上1", line.Text!, StringComparison.Ordinal);

            // And it has room to be read: a line that is on the page but has been squeezed down to
            // nothing is a line nobody sees.
            Assert.True(line.Bounds.Width > 0);
            Assert.True(line.Bounds.Height > 0);
        }));
    }

    /// <summary>
    /// The lines under a function are the places it has, and each says in brackets what goes in it:
    /// a reader who does not know what <c>start</c> means still knows where 起始位置 goes, and the
    /// unnamed piece of code is one hover away for whoever does want it.
    /// </summary>
    [Fact]
    public void The_places_under_a_function_are_named_in_brackets()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var builder = Builder("substring($a, 1)", "a");

            Assert.Equal(new[] { "[原文本]", "[起始位置]" },
                new[] { builder.Rows[1].Label, builder.Rows[2].Label });

            // The hint still names the argument the way the signature writes it, which is what a
            // reader needs to match a line to the manual.
            Assert.StartsWith("start", builder.Rows[2].LabelHint, StringComparison.Ordinal);
        }));
    }

    [Fact]
    public void A_function_with_more_than_one_argument_hands_the_next_one_the_pick()
    {
        Ui.Run(() =>
        {
            var builder = Empty("count");
            Land(builder, "var:count");
            Land(builder, "fn:if");

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
            var builder = Empty("count");
            Land(builder, "var:count");
            Land(builder, "fn:now");

            Assert.Equal("now()", builder.Preview);
            Assert.True(builder.CanConfirm);
        });
    }

    [Fact]
    public void A_function_can_take_the_arguments_its_signature_marks_out()
    {
        Ui.Run(() =>
        {
            var builder = Empty();
            Land(builder, "fn:substring");

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
            var builder = Empty("a");
            Land(builder, "var:a");
            Land(builder, "op:" + Operator(builder, Strings.Get("ExprBuilder.Tree.List")).Token);

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
            var builder = Empty();
            Land(builder, "fn:substring");
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
    public void The_handwritten_page_reaches_the_structure_only_when_it_is_built()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$count + 1", "count");
            builder.ShowTextCommand.Execute(null);
            Assert.True(builder.IsTextPage);
            Assert.Equal("$count + 1", builder.Written);

            // Typing is kept in the buffer, not read into the tree.
            builder.Written = "upper($count)";
            builder.ShowTreeCommand.Execute(null);

            Assert.True(builder.IsTreePage);
            Assert.Equal("$count + 1", builder.Preview);
            Assert.False(builder.CanConfirm);

            builder.ShowTextCommand.Execute(null);
            builder.RebuildCommand.Execute(null);

            Assert.True(builder.IsTreePage);
            Assert.False(builder.TextUnreadable);
            Assert.Equal("upper($count)", builder.Preview);
            Assert.Equal(2, builder.Rows.Count);
            Assert.True(builder.CanConfirm);
        });
    }

    /// <summary>
    /// Half a formula in the buffer is not a reason to hand back something else: the dialog waits for
    /// either the buffer to be built or the formula to be left as it was.
    /// </summary>
    [Fact]
    public void A_half_written_formula_holds_the_dialog_back_until_it_is_built()
    {
        Ui.Run(() =>
        {
            var builder = Builder("$a + 1", "a");
            builder.ShowTextCommand.Execute(null);
            builder.Written = "1 +";

            Assert.False(builder.CanConfirm);

            builder.Written = "1 + 2";
            Assert.False(builder.CanConfirm);

            builder.RebuildCommand.Execute(null);
            Assert.True(builder.IsTreePage);
            Assert.Equal("1 + 2", builder.Preview);
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
            Assert.Equal(ExpressionText.Heading(Function(builder, "upper").Function),
                builder.Rows[2].Token);
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
            var builder = Empty("a");
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

    /// <summary>
    /// An empty box under a function says what that argument wants, rather than the same "a number or
    /// a piece of text" on every line: the box standing for a regular expression has to say so.
    /// </summary>
    [Fact]
    public void An_empty_argument_says_what_goes_in_it()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var builder = Empty("a");
            Land(builder, "var:a");
            Land(builder, "fn:match");

            Assert.Equal("match($a, ▢)", builder.Preview);
            Assert.Equal(Strings.Get("Expr.Arg.pattern.put"), builder.Rows[2].HoleHint);

            // A place that is nobody's argument keeps the plain word: an operand of `+` may be
            // either kind of thing.
            var sum = Empty("a");
            Land(sum, "var:a");
            Land(sum, "op:" + Operator(sum, "+").Token);
            Assert.Equal(Strings.Get("ExprBuilder.Tree.Hole"), sum.Rows[2].HoleHint);
        }));
    }

    /// <summary>
    /// An operator and a plain value are placed the way everything else is: by being dragged onto the
    /// line they belong to. Without that, a page that only places by dragging would have no way to
    /// put a <c>+</c> or a <c>true</c> into a formula at all.
    /// </summary>
    [Fact]
    public void An_operator_and_a_value_are_placed_by_being_dragged()
    {
        Ui.Run(() =>
        {
            var builder = Empty("a", "b");

            builder.Drop(builder.Rows[0], "var:a");
            builder.Drop(builder.Rows[0], "op:+");
            Assert.Equal("$a + ▢", builder.Preview);

            // The lines run root first, so the place at the end of the sum is the third line.
            builder.Drop(builder.Rows[2], "var:b");
            Assert.Equal("$a + $b", builder.Preview);

            // A value card is dropped on the line it should stand in for: `true`, not a place to type.
            var yes = builder.Values.First(choice => choice.Kind == ValueChoiceKind.Constant);
            builder.Drop(builder.Rows[2], "val:" + builder.PayloadOf(yes));
            Assert.Equal("$a + true", builder.Preview);

            // A number card dropped on a place leaves it to be typed in, and says how it is read.
            var number = builder.Values.First(choice => choice.Kind == ValueChoiceKind.Number);
            builder.Drop(builder.Rows[2], "val:" + builder.PayloadOf(number));
            Assert.Equal("$a + ▢", builder.Preview);
            Assert.True(builder.Rows[2].IsTyped);
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

            // An operator and a plain value carry what they are the same way, so a drag from either
            // is as good as a drag from the variable wall.
            var op = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.DataContext is TreeOperator { Token: "+" });
            Assert.Equal("op:+", window.DragPayload(op));

            var yes = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.DataContext is ValueChoice { Kind: ValueChoiceKind.Constant });
            Assert.Equal("val:" + builder.PayloadOf((ValueChoice)yes.DataContext!), window.DragPayload(yes));

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
