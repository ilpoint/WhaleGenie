using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;


namespace WhaleGenie.Tests;

// Taking a piece away, and putting one on the end or in front of an argument.
public partial class ValuePickerTests
{
    /// <summary>Undo goes back a step at a time, and redo returns to where it was.</summary>
    [Fact]
    public void Undo_and_redo_walk_the_line()
    {
        var form = new ValuePickerViewModel("$a", Variables(), ActionValueForm.Value);

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
}
