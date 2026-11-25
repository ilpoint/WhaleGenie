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
            Assert.Single(found.Macros);
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
}
