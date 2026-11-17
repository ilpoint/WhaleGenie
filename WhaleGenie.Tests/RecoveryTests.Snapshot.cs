using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// What the snapshot holds: the list, the shared values, the editor and a declaration.
public partial class RecoveryTests
{
    [Fact]
    public void The_macro_list_survives_a_stop_in_the_snapshot()
    {
        // Reading a step back runs it past the action catalogue, which builds its icons with
        // Avalonia geometry, so the round trip belongs on the user-interface thread.
        InOwnFile(_ => Ui.Run(() =>
        {
            var macro = Sample();
            macro.IsEnabled = false;

            RecoveryStore.SaveProject([macro], new ProjectTriggers(), [], @"C:\work\macros.wgmacro");

            var found = RecoveryStore.Load();

            Assert.NotNull(found);
            var only = Assert.Single(found!.Macros);
            Assert.Equal("login", only.Name);
            Assert.False(only.IsEnabled);
            Assert.Equal("input.keyPress", Assert.Single(only.Steps).Type);
            Assert.Equal(@"C:\work\macros.wgmacro", found.PackagePath);
            Assert.Null(found.Editor);
        }));
    }

    [Fact]
    public void The_shared_variables_come_back_with_the_list()
    {
        InOwnFile(_ =>
        {
            var variable = new VariableDefinition
            {
                Name = "notes",
                Scope = VariableScope.Global,
                Type = ValueKind.Text,
                Default = Formula.Text("hi"),
                Description = "what to say",
            };

            // A package is what makes an empty list something worth recovering; without one there
            // would be nothing to lose.
            RecoveryStore.SaveProject([], new ProjectTriggers(), [variable], @"C:\work\macros.wgmacro");

            var found = RecoveryStore.Load();

            Assert.NotNull(found);
            Assert.Empty(found!.Macros);
            var only = Assert.Single(found.Globals);
            Assert.Equal("notes", only.Name);
            Assert.Equal(VariableScope.Global, only.Scope);
            Assert.Equal("hi", only.DefaultValue);
            Assert.Equal("what to say", only.Description);
        });
    }

    [Fact]
    public void The_project_and_the_open_editor_are_kept_side_by_side()
    {
        InOwnFile(_ =>
        {
            RecoveryStore.SaveProject([Named("login")], new ProjectTriggers(), [], null);
            RecoveryStore.SaveEditor(Named("draft"), [], "login");

            var both = RecoveryStore.Load();

            Assert.NotNull(both);
            Assert.Single(both!.Macros);
            Assert.NotNull(both.Editor);
            Assert.Equal("draft", both.Editor!.Macro.Name);
            Assert.Equal("login", both.Editor.Replaces);

            // The editor handing its macro over, or being let go, must leave the list alone.
            RecoveryStore.ClearEditor();

            var onlyList = RecoveryStore.Load();
            Assert.NotNull(onlyList);
            Assert.Single(onlyList!.Macros);
            Assert.Null(onlyList.Editor);

            // And the other way round: saving the list must not take the editor's draft with it.
            RecoveryStore.SaveEditor(Named("draft"), [], string.Empty);
            RecoveryStore.ClearProject();

            var onlyEditor = RecoveryStore.Load();
            Assert.NotNull(onlyEditor);
            Assert.Empty(onlyEditor!.Macros);
            Assert.Null(onlyEditor.PackagePath);
            Assert.NotNull(onlyEditor.Editor);
            Assert.Equal(string.Empty, onlyEditor.Editor!.Replaces);
        });
    }

    [Fact]
    public void An_unfinished_declaration_comes_back_from_the_snapshot()
    {
        InOwnFile(_ =>
        {
            var macro = Named("draft");
            macro.Parameters.Add(new MacroParameter { Name = string.Empty });
            macro.Results.Add(new MacroResult { Name = string.Empty });
            RecoveryStore.SaveEditor(macro, [], string.Empty);

            var found = RecoveryStore.Load();

            Assert.NotNull(found?.Editor);
            Assert.Single(found!.Editor!.Macro.Parameters);
            Assert.Single(found.Editor.Macro.Results);
            Assert.Equal(string.Empty, found.Editor.Macro.Parameters[0].Name);
        });
    }
}
