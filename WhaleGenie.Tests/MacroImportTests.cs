using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            WithGlobals(() =>
            {
                var macro = Reads("循环");
                var contents = Contents([macro], [Global("次数", "number", "1"), Global("无关", "text", "x")]);
                var viewModel = new MainViewModel();

                var decisions = MacroImport.Decisions([macro], contents.Globals);
                var report = MacroImport.IntoProject(viewModel, contents, [macro], decisions);

                Assert.Single(report.Added);
                Assert.Contains(VariableCatalog.Globals, variable => variable.Name == "次数");
                Assert.DoesNotContain(VariableCatalog.Globals, variable => variable.Name == "无关");
            });
        });
    }

    [Fact]
    public void A_name_this_project_already_has_is_renamed_and_said_out_loud()
    {
        Ui.Run(() =>
        {
            WithGlobals(() =>
            {
                var viewModel = new MainViewModel();
                viewModel.AddMacro(new MacroItem { Name = "整理" });

                var arriving = new MacroItem { Name = "整理" };
                var report = MacroImport.IntoProject(viewModel, Contents([arriving], []), [arriving], []);

                var pair = Assert.Single(report.RenamedMacros);
                Assert.Equal("整理", pair.Was);
                Assert.NotEqual("整理", pair.Now);
                Assert.Equal(2, viewModel.Macros.Count);
            });
        });
    }

    /// <summary>
    /// Renaming is the first answer because it keeps both meanings: the project's number is the
    /// project's, and the arriving macro reads the one that came with it.
    /// </summary>
    [Fact]
    public void A_variable_in_both_places_can_be_renamed_and_the_macro_follows_it()
    {
        Ui.Run(() =>
        {
            WithGlobals(() =>
            {
                VariableCatalog.Globals.Add(Global("次数", "number", "5"));
                var macro = Reads("循环");
                var contents = Contents([macro], [Global("次数", "number", "1")]);

                var decisions = MacroImport.Decisions([macro], contents.Globals);
                var decision = Assert.Single(decisions);
                Assert.True(decision.HasConflict);
                Assert.Equal(GlobalChoice.Rename, decision.Choice);

                MacroImport.IntoProject(new MainViewModel(), contents, [macro], decisions);

                Assert.Equal("5", VariableCatalog.Globals.Single(v => v.Name == "次数").DefaultValue);
                var arrived = VariableCatalog.Globals.Single(variable => variable.Name != "次数");
                Assert.Equal("1", arrived.DefaultValue);
                Assert.Equal($"${arrived.Name}", macro.Steps[0].Parameters[0].Value);
            });
        });
    }

    [Fact]
    public void A_variable_in_both_places_can_be_replaced_by_the_files_declaration()
    {
        Ui.Run(() =>
        {
            WithGlobals(() =>
            {
                VariableCatalog.Globals.Add(Global("次数", "number", "5"));
                var macro = Reads("循环");
                var contents = Contents([macro], [Global("次数", "number", "1")]);

                var decisions = MacroImport.Decisions([macro], contents.Globals);
                decisions[0].Choice = GlobalChoice.Overwrite;

                MacroImport.IntoProject(new MainViewModel(), contents, [macro], decisions);

                var existing = Assert.Single(VariableCatalog.Globals);
                Assert.Equal("1", existing.DefaultValue);
                Assert.Equal("$次数", macro.Steps[0].Parameters[0].Value);
            });
        });
    }

    [Fact]
    public void A_variable_in_both_places_can_keep_the_projects_declaration()
    {
        Ui.Run(() =>
        {
            WithGlobals(() =>
            {
                VariableCatalog.Globals.Add(Global("次数", "number", "5"));
                var macro = Reads("循环");
                var contents = Contents([macro], [Global("次数", "number", "1")]);

                var decisions = MacroImport.Decisions([macro], contents.Globals);
                decisions[0].Choice = GlobalChoice.UseProject;

                var report = MacroImport.IntoProject(new MainViewModel(), contents, [macro], decisions);

                Assert.Equal("5", Assert.Single(VariableCatalog.Globals).DefaultValue);
                Assert.Equal("$次数", macro.Steps[0].Parameters[0].Value);
                Assert.Equal(["次数"], report.GlobalsKept);
            });
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
            WithGlobals(() =>
            {
                var viewModel = new MainViewModel();
                var existing = new MacroItem { Name = "整理" };
                existing.Triggers.Add(new TriggerEntry
                {
                    Kind = TriggerKind.Key,
                    MacroName = "整理",
                    BindKey = "Ctrl+Alt+1",
                });
                viewModel.AddMacro(existing);

                var arriving = new MacroItem { Name = "新的" };
                arriving.Triggers.Add(new TriggerEntry
                {
                    Kind = TriggerKind.Key,
                    MacroName = "新的",
                    BindKey = "Ctrl+Alt+1",
                });

                var report = MacroImport.IntoProject(viewModel, Contents([arriving], []), [arriving], []);

                Assert.Single(report.Added);
                Assert.NotEmpty(report.DuplicateTriggers);
            });
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

    private static void WithGlobals(Action body)
    {
        var previous = VariableCatalog.Globals.ToList();
        VariableCatalog.Globals.Clear();
        try
        {
            body();
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var variable in previous)
            {
                VariableCatalog.Globals.Add(variable);
            }
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
                new StepParameter { Name = "ms", Kind = ActionParameterKind.Text, Value = "$次数" },
            ],
        });
        return macro;
    }

    private static VariableDefinition Global(string name, string type, string value) => new()
    {
        Name = name,
        Scope = VariableScope.Global,
        Type = type,
        DefaultValue = value,
    };

    private static PackageContents Contents(IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals)
        => new() { Macros = macros, Globals = globals, Triggers = new ProjectTriggers() };
}
