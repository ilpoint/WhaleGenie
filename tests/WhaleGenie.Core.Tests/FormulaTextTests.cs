using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// How a shape is said as a word and as a line. A name answers with itself where a word is asked
/// for and keeps the marks the interface passes in where a line is drawn; a field that names nothing
/// is prose. These are the readers the editor and the catalogue use, so a change here is a change to
/// what a macro author sees.
/// </summary>
public class FormulaTextTests
{
    [Fact]
    public void A_name_is_itself_as_a_word_and_only_the_marks_around_it_in_a_line()
    {
        var name = Formula.Name("count");

        Assert.Equal("count", Formula.Word(name));
        Assert.Equal("count", Formula.Speak(name));
        Assert.Equal("⟦count⟧", Formula.Speak(name, "⟦", "⟧"));
        Assert.Equal(string.Empty, Formula.Plain(name));
        Assert.False(Formula.IsPlain(name));
    }

    [Fact]
    public void Prose_and_numbers_are_said_as_written()
    {
        Assert.Equal("hello", Formula.Word(Formula.Text("hello")));
        Assert.Equal("hello", Formula.Speak(Formula.Text("hello")));
        Assert.Equal("hello", Formula.Plain(Formula.Text("hello")));
        Assert.True(Formula.IsPlain(Formula.Text("hello")));

        var number = Formula.Number("3.50", 3.5);
        Assert.Equal("3.50", Formula.Word(number));
        Assert.Equal("3.50", Formula.Speak(number));
        Assert.Equal("3.50", Formula.Plain(number));
        Assert.True(Formula.IsPlain(number));

        Assert.Equal("2026-11-12", Formula.Plain(Formula.Date("2026-11-12")));
        Assert.True(Formula.IsPlain(Formula.Date("2026-11-12")));
        Assert.Equal("true", Formula.Plain(Formula.Constant("true")));
        Assert.True(Formula.IsPlain(Formula.Constant("true")));
    }

    [Fact]
    public void A_run_is_the_pieces_next_to_each_other()
    {
        var run = Formula.Run([Formula.Text("a"), Formula.Name("x"), Formula.Number(1)]);

        Assert.Equal("ax1", Formula.Word(run));
        Assert.Equal("a⟦x⟧1", Formula.Speak(run, "⟦", "⟧"));
        Assert.Equal("a1", Formula.Plain(run));
        Assert.False(Formula.IsPlain(run));
        Assert.True(Formula.IsPlain(Formula.Run([Formula.Text("a"), Formula.Number(1)])));
    }

    [Fact]
    public void A_formula_is_drawn_as_the_formula_it_is_and_names_nothing()
    {
        var call = Formula.Call("upper", Formula.Name("who"));

        Assert.Equal("upper(⟦who⟧)", Formula.Speak(call, "⟦", "⟧"));
        Assert.Equal("upper(who)", Formula.Word(call));
        Assert.Equal(string.Empty, Formula.Plain(call));
        Assert.False(Formula.IsPlain(call));

        Assert.Equal("not ⟦ready⟧", Formula.Speak(Formula.Unary("not", Formula.Name("ready")), "⟦", "⟧"));
        Assert.Equal("-3", Formula.Speak(Formula.Unary("-", Formula.Number(3))));
        Assert.Equal("1 + 2", Formula.Speak(Formula.Binary("+", Formula.Number(1), Formula.Number(2))));
        Assert.Equal("(1)", Formula.Speak(new GroupNode(Formula.Number(1))));
        Assert.Equal("[1, 2]", Formula.Speak(Formula.List(Formula.Number(1), Formula.Number(2))));
        Assert.Equal(string.Empty, Formula.Plain(Formula.List(Formula.Number(1))));
        Assert.False(Formula.IsPlain(Formula.List(Formula.Number(1))));
    }

    [Fact]
    public void A_group_says_its_inner_shape_with_or_without_the_brackets()
    {
        var group = new GroupNode(Formula.Name("x"));

        Assert.Equal("x", Formula.Word(group));
        Assert.Equal("(⟦x⟧)", Formula.Speak(group, "⟦", "⟧"));
        Assert.Equal(string.Empty, Formula.Plain(group));
    }

    [Fact]
    public void The_name_a_field_names_is_the_name_and_nothing_around_it()
    {
        Assert.Equal("count", Formula.NameOf(Formula.Name(" count ")));
        Assert.Equal("hello", Formula.NameOf(Formula.Text(" hello ")));
        Assert.Equal("3.50", Formula.NameOf(Formula.Number(" 3.50 ", 3.5)));
        Assert.Equal("2026-11-12", Formula.NameOf(Formula.Date(" 2026-11-12 ")));
        Assert.Equal("x", Formula.NameOf(new GroupNode(Formula.Name(" x "))));
        Assert.Equal("(x + 1)", Formula.NameOf(Formula.Text("(x + 1)")));
        Assert.Equal(string.Empty, Formula.NameOf(Formula.Binary("+", Formula.Name("x"), Formula.Number(1))));
        Assert.Equal(string.Empty, Formula.NameOf(null));
    }

    [Fact]
    public void A_field_that_says_nothing_is_the_empty_text_and_a_space_is_a_word()
    {
        Assert.True(Formula.IsNothing(null));
        Assert.True(Formula.IsNothing(Formula.Text(string.Empty)));
        Assert.False(Formula.IsNothing(Formula.Text(" ")));
        Assert.False(Formula.IsNothing(Formula.Name(string.Empty)));
        Assert.Equal(string.Empty, Formula.Word(Formula.Hole));
        Assert.Equal(string.Empty, Formula.Plain(Formula.Hole));
        Assert.False(Formula.IsPlain(Formula.Hole));
    }
}
