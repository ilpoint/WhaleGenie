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
        var form = new ValuePickerViewModel(Formula.Binary(">", Formula.Name("count"), Formula.Hole), Variables(), ActionValueForm.Value);

        form.SetTypingMode(number: true);
        form.TypingText = "3.5";

        Assert.True(form.CanInsertTyped);
        Assert.Equal("3.5", form.TypedToken());
    }

    /// <summary>Text goes in quoted, with what is inside it escaped.</summary>
    [Fact]
    public void Text_is_put_in_between_quotes_with_what_is_inside_it_escaped()
    {
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(), ActionValueForm.Value);

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
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(), ActionValueForm.Value);

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
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(), ActionValueForm.Value);

        form.SetTypingMode(number: true);
        form.TypingText = typed;

        var token = form.TypedToken();
        Assert.NotNull(token);

        form.Place(token);
        var shape = Answer(form);
        Assert.True(shape is NumberNode or UnaryNode { Operator: "-", Operand: NumberNode });
    }

    /// <summary>An empty piece of text is not a material: there is nothing to put in.</summary>
    [Fact]
    public void An_empty_text_has_nothing_to_insert()
    {
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(), ActionValueForm.Value);

        form.SetTypingMode(number: false);

        Assert.Null(form.TypedToken());
        Assert.False(form.CanInsertTyped);
    }

    /// <summary>
    /// Brackets and quotes are not signs in this window. Brackets become one group token and lists
    /// become one list token, so a half pair cannot be put into the band.
    /// </summary>
    [Fact]
    public void The_strip_offers_no_bracket_or_quote_buttons()
    {
        var form = new ValuePickerViewModel(Formula.Nothing, Variables(), ActionValueForm.Value);

        var tokens = form.Operators.Select(one => one.Token).ToList();

        Assert.DoesNotContain("(", tokens);
        Assert.DoesNotContain(")", tokens);
        Assert.DoesNotContain("[", tokens);
        Assert.DoesNotContain("]", tokens);
        Assert.DoesNotContain("\"", tokens);
        Assert.DoesNotContain("( )", tokens);
    }
}
