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
        var form = new ValuePickerViewModel("count", Variables(), ActionReadMode.WholeValue,
            bareNames: true);

        Assert.True(form.Reads);
        Assert.True(form.CanConfirm);
        Assert.False(form.NoteIsProblem);
    }

    /// <summary>
    /// A whole value that is not a formula is the text it looks like: a path is not a mistake.
    /// </summary>
    [Fact]
    public void A_value_field_takes_a_sentence_it_cannot_read_as_a_formula()
    {
        var form = new ValuePickerViewModel(@"C:\a b", Variables(), ActionReadMode.WholeValue);

        Assert.True(form.Reads);
        Assert.True(form.CanConfirm);
        Assert.False(form.NoteIsProblem);
    }

    /// <summary>A field that holds one name is not asked whether the name is a formula.</summary>
    [Fact]
    public void A_name_field_never_reports_a_reading_problem()
    {
        var form = new ValuePickerViewModel("count", Variables(), ActionReadMode.VariableName);

        Assert.False(form.NoteIsProblem);
        Assert.Equal(string.Empty, form.Note);
    }

    /// <summary>An empty line is how a cell is cleared without leaving the window.</summary>
    [Fact]
    public void An_empty_cell_can_be_confirmed_so_the_window_can_clear_it()
    {
        var form = new ValuePickerViewModel(null, Variables(), ActionReadMode.WholeValue);
        ValuePick? closed = null;
        form.CloseRequested += value => closed = value;

        Assert.True(form.CanConfirm);
        Assert.Equal(string.Empty, form.BlockReason);

        form.Confirm();

        Assert.True(closed!.Chosen);
        Assert.Equal(Formula.Nothing, closed.Value);
    }

    /// <summary>
    /// The window and the field must give the same answer for the same line. A line that opens as a
    /// value and then fails stays a mistake; a line that is prose stays prose.
    /// </summary>
    [Theory]
    [InlineData("count", true, false, true)]
    [InlineData("count", false, false, true)]
    [InlineData("$count + 1", true, false, true)]
    [InlineData("$count +", true, false, false)]
    [InlineData("$count +", false, false, false)]
    [InlineData(@"C:\a b", false, false, true)]
    [InlineData("hello $name", false, true, true)]
    public void The_seed_reads_in_the_same_way_the_field_will_read_it(
        string seed, bool bareNames, bool textMode, bool reads)
    {
        var form = new ValuePickerViewModel(seed, Variables(),
            textMode ? ActionReadMode.Text : ActionReadMode.WholeValue, bareNames);

        Assert.Equal(reads, form.Reads);
        Assert.Equal(reads, form.CanConfirm);
    }
}
