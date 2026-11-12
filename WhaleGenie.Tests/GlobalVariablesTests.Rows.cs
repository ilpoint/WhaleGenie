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

// An empty project against an empty filter, and a hidden row that cannot be saved.
public partial class GlobalVariablesTests
{
    [Fact]
    public void Editing_a_name_keeps_the_selected_row_visible_under_an_old_search()
    {
        var viewModel = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "oldName",
                Scope = VariableScope.Global,
            },
        ]);
        viewModel.SearchText = "old";
        var row = Assert.IsType<VariableDefinitionDraft>(viewModel.Selected);

        row.Name = "newName";

        Assert.Same(row, viewModel.Selected);
        Assert.Contains(row, viewModel.Shown);
        Assert.True(viewModel.HasChanges);
        row.Name = "oldName";
        Assert.False(viewModel.HasChanges);
    }

    [Fact]
    public void Empty_state_tells_an_empty_project_from_an_empty_filter()
    {
        var empty = new GlobalVariablesViewModel([]);
        Assert.True(empty.ShowsNoVariables);
        Assert.False(empty.ShowsNoMatches);
        Assert.Equal(Strings.Get("GlobalVariables.NoVariables"), empty.EmptyMessage);

        var filtered = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "total",
                Scope = VariableScope.Global,
            },
        ]);
        filtered.SearchText = "missing";
        Assert.False(filtered.ShowsNoVariables);
        Assert.True(filtered.ShowsNoMatches);
        Assert.Equal(Strings.Get("GlobalVariables.NoMatches"), filtered.EmptyMessage);

        filtered.ClearFiltersCommand.Execute(null);
        Assert.False(filtered.ShowsNoMatches);
        Assert.Single(filtered.Shown);
    }

    [Fact]
    public void Saving_an_invalid_hidden_row_reveals_and_selects_its_problem()
    {
        var viewModel = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "visible",
                Scope = VariableScope.Global,
            },
            new VariableDefinition
            {
                Name = "hidden",
                Scope = VariableScope.Global,
                Type = ValueKind.Number,
                DefaultValue = "not a number",
            },
        ]);
        GlobalVariableProblemField? focus = null;
        var blocked = false;
        IReadOnlyList<VariableDefinitionDraft>? saved = null;
        viewModel.FocusRequested += field => focus = field;
        viewModel.SaveBlocked += () => blocked = true;
        viewModel.Saved += rows => saved = rows;
        viewModel.SearchText = "visible";

        viewModel.SaveCommand.Execute(null);

        Assert.True(blocked);
        Assert.Null(saved);
        Assert.Equal(string.Empty, viewModel.SearchText);
        Assert.Equal(GlobalVariableFilterKind.All, viewModel.SelectedFilter!.Kind);
        Assert.Equal("hidden", viewModel.Selected!.Name);
        Assert.Equal(GlobalVariableProblemField.DefaultValue, focus);
    }
}
