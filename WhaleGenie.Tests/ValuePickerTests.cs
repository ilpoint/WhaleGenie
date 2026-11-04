using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The one window that fills a cell. A name, a value, a sign and a function are the same kind of
/// thing here — something that goes into the formula at the caret — so the model is judged on the
/// things the three panels must be able to promise: a piece lands where the person was typing, the
/// panels narrow to what is asked for, and a formula is only handed back when it reads.
/// </summary>
public class ValuePickerTests
{
    private static IReadOnlyList<VariableChoice> Variables() =>
    [
        new VariableChoice { Name = "count", Scope = VariableScope.Local, Type = "number" },
        new VariableChoice { Name = "title", Scope = VariableScope.Local, Type = "text" },
    ];

    [Fact]
    public void A_piece_goes_in_at_the_caret_rather_than_over_the_line()
    {
        var form = new ValuePickerViewModel("$count + 1", Variables(), namesOnly: false);

        var caret = form.Insert(" * 2", 6);

        Assert.Equal("$count * 2 + 1", form.Text);
        Assert.Equal(10, caret);
    }

    [Fact]
    public void A_bracketed_pair_opens_between_its_two_halves()
    {
        var form = new ValuePickerViewModel(string.Empty, Variables(), namesOnly: false);

        var caret = form.Insert("upper()", 0, back: 1);

        Assert.Equal("upper()", form.Text);
        Assert.Equal(6, caret);
        Assert.Equal(1, ValuePickerViewModel.BackIntoTokens("()"));
    }

    [Fact]
    public void The_function_panel_narrows_to_what_is_typed_and_to_the_group_chosen()
    {
        var form = new ValuePickerViewModel(null, Variables(), namesOnly: false);

        form.FunctionQuery = "大写";
        Assert.Contains(form.Functions, function => function.Name == "upper");
        Assert.DoesNotContain(form.Functions, function => function.Name == "length");

        // The group is a second narrowing on the same list, not a different list.
        form.FunctionQuery = string.Empty;
        form.SelectedFunctionGroup = form.FunctionGroups.First(group => group.Group == "text");
        Assert.NotEmpty(form.Functions);
        Assert.All(form.Functions, function => Assert.Equal("text", function.Group));
    }

    [Fact]
    public void A_cell_that_only_holds_a_name_hands_the_name_back()
    {
        var form = new ValuePickerViewModel(null, Variables(), namesOnly: true);
        string? handed = null;
        form.CloseRequested += result => handed = result;

        form.Wall.Looking = form.Wall.Shown.First(choice => choice.Name == "title");
        form.Confirm();

        Assert.Equal("$title", handed);
        Assert.False(form.CanCompute);
        Assert.True(form.CanConfirm);
    }

    [Fact]
    public void A_formula_that_does_not_read_says_so_and_cannot_be_handed_back()
    {
        var form = new ValuePickerViewModel("1 +", Variables(), namesOnly: false);

        Assert.True(form.NoteIsProblem);
        Assert.False(form.Reads);
        Assert.False(form.CanConfirm);

        form.Text = "1 + 2";

        Assert.False(form.NoteIsProblem);
        Assert.True(form.Reads);
        Assert.True(form.CanConfirm);
    }
}
