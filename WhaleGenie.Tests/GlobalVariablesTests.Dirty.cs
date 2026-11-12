using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Whether there is anything to save, and the row the list keeps in view.
public partial class GlobalVariablesTests
{
    [Fact]
    public void Dirty_state_follows_actual_changes()
    {
        var existing = new VariableDefinition
        {
            Name = "total",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            DefaultValue = "1",
            Description = "running total",
            PersistValue = true,
        };
        var viewModel = new GlobalVariablesViewModel([existing]);
        var row = Assert.Single(viewModel.Rows);
        Assert.False(viewModel.HasChanges);

        row.Description = "changed";
        Assert.True(viewModel.HasChanges);
        row.Description = "running total";
        Assert.False(viewModel.HasChanges);

        // The current value is what a run put there: shown, never typed, and nothing here can dirty
        // the row through it.
        Assert.Equal("1", row.CurrentValue);

        row.PersistValue = false;
        Assert.True(viewModel.HasChanges);
        row.PersistValue = true;
        Assert.False(viewModel.HasChanges);

        viewModel.NewCommand.Execute(null);
        Assert.True(viewModel.HasChanges);
        viewModel.RemoveCommand.Execute(null);
        Assert.False(viewModel.HasChanges);

        viewModel.Selected = row;
        viewModel.RemoveCommand.Execute(null);
        Assert.True(viewModel.HasChanges);
    }
}
