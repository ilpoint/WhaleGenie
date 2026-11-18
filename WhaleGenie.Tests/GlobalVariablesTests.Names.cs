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

// The name a global may take: duplicates, reserved words, blanks and numbers.
public partial class GlobalVariablesTests
{
    [Fact]
    public void A_duplicate_or_reserved_name_blocks_save()
    {
        var viewModel = new GlobalVariablesViewModel(
        [
            new VariableDefinition { Name = "total", Scope = VariableScope.Global },
        ]);
        viewModel.NewCommand.Execute(null);
        viewModel.Selected!.Name = "TOTAL";
        Assert.False(viewModel.CanSave);
        Assert.NotEqual(string.Empty, viewModel.ValidationMessage);

        viewModel.Selected.Name = "sys.fake";
        Assert.False(viewModel.CanSave);
        Assert.NotEqual(string.Empty, viewModel.ValidationMessage);
    }

    [Fact]
    public void A_new_blank_row_waits_for_a_name_without_calling_it_unreadable()
    {
        var viewModel = new GlobalVariablesViewModel([]);

        viewModel.NewCommand.Execute(null);

        Assert.NotNull(viewModel.Selected);
        Assert.Equal(string.Empty, viewModel.Selected.Name);
        Assert.False(viewModel.CanSave);
        Assert.True(viewModel.HasValidationProblem);
        Assert.True(viewModel.NeedsName);
        Assert.Equal(Strings.Get("GlobalVariables.NameRequired"), viewModel.ValidationMessage);

        viewModel.Selected.Name = "not a name";
        Assert.True(viewModel.HasValidationProblem);
    }

    [Fact]
    public void A_numeric_name_can_be_saved()
    {
        var viewModel = new GlobalVariablesViewModel([]);
        IReadOnlyList<VariableDefinitionDraft>? saved = null;
        viewModel.Saved += rows => saved = rows;

        viewModel.NewCommand.Execute(null);
        viewModel.Selected!.Name = "123";
        viewModel.Selected.Default = Formula.Text("7");

        Assert.True(viewModel.CanSave, viewModel.ValidationMessage);
        viewModel.SaveCommand.Execute(null);

        var row = Assert.Single(saved!);
        Assert.Equal("123", row.Name);
        Assert.Equal("7", row.DefaultValue);
    }
}
