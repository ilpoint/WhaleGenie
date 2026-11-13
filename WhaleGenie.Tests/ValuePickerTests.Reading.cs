using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Stepping along the line, and the whole formula at once.
public partial class ValuePickerTests
{
    /// <summary>
    /// The places the line can stand are the piece edges, not every character: the band only ever
    /// draws the line on an edge, so a step inside a name would look like nothing happened.
    /// </summary>
    [Fact]
    public void The_line_steps_between_piece_edges_rather_than_characters()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionValueForm.Value);

        Assert.Equal(new[] { 0, 2, 3, 4, 5, 7 }, form.Seams());

        Assert.True(form.StepCaret(-1));
        Assert.Equal(5, form.Caret);
        Assert.True(form.StepCaret(-1));
        Assert.Equal(4, form.Caret);
        Assert.True(form.StepCaret(1));
        Assert.Equal(5, form.Caret);
    }

    [Fact]
    public void A_step_past_either_end_stays_where_it_is()
    {
        var form = new ValuePickerViewModel("$a", Variables(), ActionValueForm.Value);

        form.SetCaret(0);
        Assert.False(form.StepCaret(-1));
        Assert.Equal(0, form.Caret);

        form.SetCaret(form.Text.Length);
        Assert.False(form.StepCaret(1));
        Assert.Equal(form.Text.Length, form.Caret);
    }

    /// <summary>An empty formula is one place, so no arrow key has anywhere to go.</summary>
    [Fact]
    public void An_empty_formula_has_one_place_and_no_step()
    {
        var form = new ValuePickerViewModel(null, Variables(), ActionValueForm.Value);

        Assert.Equal(new[] { 0 }, form.Seams());
        Assert.False(form.StepCaret(-1));
        Assert.False(form.StepCaret(1));
    }

    /// <summary>Every way of writing a piece leaves the line on a place it can then step away from.</summary>
    [Fact]
    public void A_written_piece_leaves_the_line_on_a_seam()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionValueForm.Value);
        form.SetCaret(2);

        form.Place("$c");

        Assert.Contains(form.Caret, form.Seams());
    }

    /// <summary>Clearing is one more step in the same history, not a way out of it.</summary>
    [Fact]
    public void The_whole_formula_goes_at_once_and_undo_brings_it_back()
    {
        var form = new ValuePickerViewModel("$a + $b", Variables(), ActionValueForm.Value);
        Assert.True(form.CanClear);

        form.Clear();

        Assert.Equal(string.Empty, form.Text);
        Assert.Equal(0, form.Caret);
        Assert.False(form.CanClear);
        Assert.True(form.CanUndo);

        form.Undo();

        Assert.Equal("$a + $b", form.Text);
        Assert.True(form.CanClear);
    }

    [Fact]
    public void An_empty_formula_has_nothing_to_clear()
    {
        var form = new ValuePickerViewModel(null, Variables(), ActionValueForm.Value);

        Assert.False(form.CanClear);
    }

    /// <summary>One group button always stands for the list on show; the rest are plain words.</summary>
    [Fact]
    public void The_function_group_on_show_is_the_marked_one()
    {
        var form = new ValuePickerViewModel("$a", Variables(), ActionValueForm.Value);

        Assert.True(form.FunctionGroups[0].IsSelected);
        Assert.Single(form.FunctionGroups, filter => filter.IsSelected);

        form.SelectedFunctionGroup = form.FunctionGroups[1];

        Assert.False(form.FunctionGroups[0].IsSelected);
        Assert.True(form.FunctionGroups[1].IsSelected);
        Assert.Single(form.FunctionGroups, filter => filter.IsSelected);
    }
}
