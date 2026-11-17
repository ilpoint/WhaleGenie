using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// Taking a chosen part of a file into a project: names that are taken, variables that both sides
/// declare, and the one thing that must never happen — a macro arriving with a variable it thinks
/// means one thing while the project means another.
/// </summary>
public class MacroImportTests
{
    [Fact]
    public void An_imported_macro_brings_the_global_variable_it_reads()
    {
        Ui.Run(() =>
        {
            var macro = Reads("循环");
            var contents = Contents([macro], [Global("次数", "number", "1"), Global("无关", "text", "x")]);
            var viewModel = new MainViewModel();

            var decisions = MacroImport.Decisions([macro], contents.Globals,
                viewModel.Variables.Globals);
            var report = MacroImport.IntoProject(viewModel.Workspace, contents, [macro], decisions);

            Assert.Single(report.Added);
            Assert.Contains(viewModel.Variables.Globals, variable => variable.Name == "次数");
            Assert.DoesNotContain(viewModel.Variables.Globals, variable => variable.Name == "无关");
        });
    }

    [Fact]
    public void A_name_this_project_already_has_is_renamed_and_said_out_loud()
    {
        Ui.Run(() =>
        {
            var viewModel = new MainViewModel();
            viewModel.AddMacro(new MacroItem { Name = "整理" });

            var arriving = new MacroItem { Name = "整理" };
            var report = MacroImport.IntoProject(viewModel.Workspace, Contents([arriving], []), [arriving], []);

            var pair = Assert.Single(report.RenamedMacros);
            Assert.Equal("整理", pair.Was);
            Assert.NotEqual("整理", pair.Now);
            Assert.Equal(2, viewModel.Macros.Count);
        });
    }
    [Fact]
    public void A_variable_in_both_places_can_be_replaced_by_the_files_declaration()
    {
        Ui.Run(() =>
        {
            var viewModel = new MainViewModel();
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
                Guid.NewGuid().ToString("N"));
            using var persistent = new PersistedVariables(
                Path.Combine(folder, "persistent-variables.json"));
            persistent.Initialize(viewModel.Variables);
            var kept = Global("次数", "number", "5");
            kept.PersistValue = true;
            viewModel.Variables.Adopt(kept);
            var macro = Reads("循环");
            var contents = Contents([macro], [Global("次数", "number", "1")]);

            var decisions = MacroImport.Decisions([macro], contents.Globals,
                viewModel.Variables.Globals);
            decisions[0].Choice = GlobalChoice.Overwrite;

            MacroImport.IntoProject(viewModel.Workspace, contents, [macro], decisions);

            var existing = Assert.Single(viewModel.Variables.Globals);
            Assert.Equal("1", existing.DefaultValue);
            // Whether a value is kept between runs is the person's choice, and a package says
            // nothing about it: overwriting the declaration must not clear the checkbox.
            Assert.True(existing.PersistValue);
            Assert.Equal("次数", macro.Steps[0].Parameters[0].Value);
        });
    }

    [Fact]
    public void A_variable_in_both_places_can_keep_the_projects_declaration()
    {
        Ui.Run(() =>
        {
            var viewModel = new MainViewModel();
            viewModel.Variables.Adopt(Global("次数", "number", "5"));
            var macro = Reads("循环");
            var contents = Contents([macro], [Global("次数", "number", "1")]);

            var decisions = MacroImport.Decisions([macro], contents.Globals,
                viewModel.Variables.Globals);
            decisions[0].Choice = GlobalChoice.UseProject;

            var report = MacroImport.IntoProject(viewModel.Workspace, contents, [macro], decisions);

            Assert.Equal("5", Assert.Single(viewModel.Variables.Globals).DefaultValue);
            Assert.Equal("次数", macro.Steps[0].Parameters[0].Value);
            Assert.Equal(["次数"], report.GlobalsKept);
        });
    }

    /// <summary>
    /// Two macros on one hotkey is a conflict the person has to settle, so it is reported rather
    /// than refused: the file is still imported, and the entry is theirs to change.
    /// </summary>
    [Fact]
    public void A_trigger_entry_that_is_already_used_is_reported()
    {
        Ui.Run(() =>
        {
            {
                var viewModel = new MainViewModel();
                var existing = new MacroItem { Name = "整理" };
                viewModel.AddMacro(existing,
                    [new TriggerEntry { MacroName = "整理", Spec = new KeySpec("Ctrl+Alt+1") }]);

                var arriving = new MacroItem { Name = "新的" };
                var contents = Contents([arriving], [],
                    [new TriggerEntry { MacroName = "新的", Spec = new KeySpec("Ctrl+Alt+1") }]);

                var report = MacroImport.IntoProject(viewModel.Workspace, contents, [arriving], []);

                Assert.Single(report.Added);
                Assert.NotEmpty(report.DuplicateTriggers);
            }
        });
    }

    /// <summary>What is put into the public library is a callable macro, named so the library stays valid.</summary>
    [Fact]
    public void The_library_takes_a_copy_and_numbers_a_name_it_already_has()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var library = Path.Combine(folder, "library" + MacroPackage.Extension);

        try
        {
            var first = MacroImport.IntoLibrary([new MacroItem { Name = "读表" }], library);
            Assert.Equal("读表", Assert.Single(first).Macro.Name);

            var again = MacroImport.IntoLibrary([new MacroItem { Name = "读表" }], library);
            Assert.Equal("读表2", Assert.Single(again).Macro.Name);

            Assert.Equal(2, PublicMacroLibrary.Load(library).Count);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static MacroItem Reads(string name)
    {
        var macro = new MacroItem { Name = name };
        macro.Steps.Add(new MacroStep
        {
            Type = "control.delay",
            Parameters =
            [
                new StepParameter { Name = "ms", Kind = ActionParameterKind.Text, Written = Formula.Name("次数") },
            ],
        });
        return macro;
    }

    private static VariableDefinition Global(string name, string type, string value) => new()
    {
        Name = name,
        Scope = VariableScope.Global,
        Type = VariableTypes.KindOf(type),
        Default = Formula.Text(value),
    };

    private static PackageContents Contents(IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals, IReadOnlyList<TriggerEntry>? entries = null)
    {
        var triggers = new ProjectTriggers();
        foreach (var entry in entries ?? [])
        {
            triggers.Entries.Add(entry);
        }

        return new() { Macros = macros, Globals = globals, Triggers = triggers };
    }
}
