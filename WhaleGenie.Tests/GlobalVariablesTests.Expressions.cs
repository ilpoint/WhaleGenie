using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Default values as expressions, and the draft order they are read in.
public partial class GlobalVariablesTests
{
    [Fact]
    public void The_expression_builder_uses_the_current_draft_order()
    {
        Ui.Run(() =>
        {
            var window = new GlobalVariablesWindow(
            [
                new VariableDefinition
                {
                    Name = "base",
                    Scope = VariableScope.Global,
                    Type = "number",
                    DefaultValue = "2",
                },
            ], []);
            var manager = (GlobalVariablesViewModel)window.DataContext!;
            manager.NewCommand.Execute(null);
            var later = manager.Selected!;
            later.Name = "derived";

            var choices = window.ExpressionVariablesBefore(later);

            Assert.Contains(choices, choice =>
                choice.Name == "base" && choice.Group == VariableGroup.Global);
            Assert.DoesNotContain(choices, choice => choice.Name == "derived");
            manager.RemoveCommand.Execute(null);
            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void Editing_a_draft_leaves_its_existing_name_alone()
    {
        var draft = new VariableDefinitionDraft(new VariableDefinition
        {
            Name = "total",
            Scope = VariableScope.Global,
        });

        Assert.Equal("total", draft.Name);
        draft.Description = "The running total.";
        draft.PersistValue = true;

        Assert.Equal("total", draft.Name);
        Assert.Equal("The running total.", draft.ToDefinition().Description);
        Assert.True(draft.ToDefinition().PersistValue);
    }

    [Fact]
    public void Default_values_are_expressions_evaluated_in_declaration_order()
    {
        var previousGlobals = VariableCatalog.Globals.ToList();
        var previousValues = SharedVariables.Current.Values;
        VariableCatalog.Globals.Clear();
        SharedVariables.Current.Clear();
        var source = new VariableDefinition
        {
            Name = "baseCount",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "4",
        };
        var derived = new VariableDefinition
        {
            Name = "derivedCount",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "$baseCount + 1",
        };
        VariableCatalog.Globals.Add(source);
        VariableCatalog.Globals.Add(derived);
        try
        {
            var store = MacroVariables.Seed();

            Assert.Equal(4, store.Global.Values["baseCount"].AsNumber());
            Assert.Equal(5, store.Global.Values["derivedCount"].AsNumber());
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var global in previousGlobals)
            {
                VariableCatalog.Globals.Add(global);
            }

            SharedVariables.Current.Clear();
            foreach (var (name, value) in previousValues)
            {
                SharedVariables.Current.Set(name, value);
            }
        }
    }

    [Fact]
    public void The_manager_can_use_an_earlier_draft_in_a_later_default_expression()
    {
        var manager = new GlobalVariablesViewModel(
        [
            new VariableDefinition
            {
                Name = "baseCount",
                Scope = VariableScope.Global,
                Type = "number",
                DefaultValue = "3",
            },
        ]);
        manager.NewCommand.Execute(null);
        manager.Selected!.Name = "derivedCount";
        manager.Selected.Type = "number";
        manager.Selected.DefaultValue = "$baseCount + 2";

        Assert.False(manager.Selected.DefaultHasError);
        Assert.Equal("5", manager.Selected.DefaultPreview);
    }
}
