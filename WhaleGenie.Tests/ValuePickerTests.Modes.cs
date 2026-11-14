using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// How a cell is read, and whether the window checks the line the same way the field stores it.
public partial class ValuePickerTests
{
    /// <summary>
    /// A formula field stores a bare word as the name it is. Opening the window on such a cell must
    /// not call the cell unreadable just because a word alone is not prose.
    /// </summary>
    [Fact]
    public void A_formula_field_accepts_a_bare_name_as_its_seed()
    {
        var form = new ValuePickerViewModel("count", Variables(), ActionValueForm.Value,
            bareNames: true);

        Assert.True(form.Reads);
        Assert.True(form.CanConfirm);
        Assert.False(form.NoteIsProblem);
    }
    /// <summary>A field that holds one name is not asked whether the name is a formula.</summary>
    [Fact]
    public void A_name_field_never_reports_a_reading_problem()
    {
        var form = new ValuePickerViewModel("count", Variables(), ActionValueForm.Name);

        Assert.False(form.NoteIsProblem);
        Assert.Equal(string.Empty, form.Note);
    }

    /// <summary>An empty line is how a cell is cleared without leaving the window.</summary>
    [Fact]
    public void An_empty_cell_can_be_confirmed_so_the_window_can_clear_it()
    {
        var form = new ValuePickerViewModel((string?)null, Variables(), ActionValueForm.Value);
        ValuePick? closed = null;
        form.CloseRequested += value => closed = value;

        Assert.True(form.CanConfirm);
        Assert.Equal(string.Empty, form.BlockReason);

        form.Confirm();

        Assert.True(closed!.Chosen);
        Assert.Equal(Formula.Nothing, closed.Value);
    }
}
