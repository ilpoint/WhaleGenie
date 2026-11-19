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

// What the editor drafts: adding and editing a global without touching the declaration.
public partial class GlobalVariablesTests
{
    [Fact]
    public void Adding_and_editing_a_global_does_not_change_the_declaration_until_save()
    {
        var existing = new VariableDefinition
        {
            Name = "total",
            Scope = VariableScope.Global,
            Type = ValueKind.Number,
            Default = Formula.Text("1"),
            PersistValue = true,
        };
        var viewModel = new GlobalVariablesViewModel([existing]);
        IReadOnlyList<VariableDefinitionDraft>? saved = null;
        viewModel.Saved += rows => saved = rows;

        viewModel.Rows.Single().Default = Formula.Text("2");
        viewModel.NewCommand.Execute(null);
        viewModel.Selected!.Name = "count";
        viewModel.Selected.Default = Formula.Text("0");
        Assert.Equal("1", existing.DefaultValue);
        Assert.Null(saved);

        viewModel.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal(["total", "count"], saved.Select(row => row.Name));
        Assert.Equal("2", saved.Single(row => row.Name == "total").DefaultValue);
        Assert.True(saved.Single(row => row.Name == "total").PersistValue);
    }
}
