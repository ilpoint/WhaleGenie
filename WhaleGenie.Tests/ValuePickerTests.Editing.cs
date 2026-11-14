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
    /// <summary>Undo goes back one shape edit at a time, and redo returns to where it was.</summary>
    [Fact]
    public void Undo_and_redo_walk_the_shape()
    {
        var form = new ValuePickerViewModel(ValuePickerTests.Seed("a"), Variables(), ActionValueForm.Value);

        form.Place(" + ");
        form.Place("b");

        Assert.True(form.CanUndo);

        form.Undo();
        Assert.True(form.CanRedo);
        Assert.DoesNotContain(form.Recipe!.Walk().OfType<SlotValue>(), one => one.Text == "b");

        form.Redo();
        Assert.Contains(form.Recipe!.Walk().OfType<SlotValue>(), one => one.Text == "b");
    }
}
