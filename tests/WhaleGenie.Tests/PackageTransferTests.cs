using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The transfer wizard's whole logic without a window: what a package would bring, what the ticks
/// write, and the package an export hands back. The two native choosers are gone, so these are the
/// cases that keep the import and export rules from living only in the page.
/// </summary>
public class PackageTransferTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "whalegenie-tests", Guid.NewGuid().ToString("N"));

    private readonly string _library;

    private readonly PackageTransferService _transfer;

    private readonly ProjectWorkspace _project = new();

    public PackageTransferTests()
    {
        Directory.CreateDirectory(_folder);
        _library = Path.Combine(_folder, "public-macros" + MacroPackage.Extension);
        _transfer = new PackageTransferService(new FilePackageStore(),
            new FilePublicLibraryStore(_library));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public async Task An_empty_project_takes_what_the_package_holds()
    {
        var path = Package("from");
        Save(path, [new MacroItem { Name = "远程" }], []);

        var planOutcome = await _transfer.PlanImportAsync(_project, path);
        Assert.True(planOutcome.Ok);
        var plan = planOutcome.Value!;
        var row = Assert.Single(plan.Macros);
        Assert.Equal(path, plan.Source);
        Assert.Equal("from.wgmacro", plan.FileName);
        Assert.Equal("远程", row.Name);
        Assert.False(row.IsSub);
        Assert.Equal(0, row.Steps);
        Assert.Empty(row.Problem);
        Assert.Empty(plan.Globals);

        var reportOutcome = await _transfer.ApplyImportAsync(_project, path, ["远程"], []);
        Assert.True(reportOutcome.Ok);
        var report = reportOutcome.Value!;

        Assert.Contains("远程", report, StringComparison.Ordinal);
        Assert.Equal("远程", Assert.Single(_project.Macros).Name);
    }

    [Theory]
    [InlineData("keep")]
    [InlineData("overwrite")]
    [InlineData("rename")]
    public async Task A_global_both_sides_declare_is_settled_by_the_answer(string action)
    {
        _project.Variables.Adopt(Global("计数", ValueKind.Number, Formula.Number(1)));
        var path = Package("conflict");
        Save(path, [Reading("远程", "计数")],
            [Global("计数", ValueKind.Text, Formula.Text("file"))]);

        var planOutcome = await _transfer.PlanImportAsync(_project, path);
        Assert.True(planOutcome.Ok);
        var plan = planOutcome.Value!;
        var arriving = Assert.Single(plan.Globals);
        Assert.True(arriving.Conflicts);

        var applied = await _transfer.ApplyImportAsync(_project, path,
            ["远程"], [new ImportDecision("计数", action)]);
        Assert.True(applied.Ok);

        var macro = Assert.Single(_project.Macros);
        switch (action)
        {
            case "keep":
                Assert.Equal(ValueKind.Number,
                    Assert.Single(_project.Variables.Globals).Type);
                break;

            case "overwrite":
                var replaced = Assert.Single(_project.Variables.Globals);
                Assert.Equal(ValueKind.Text, replaced.Type);
                Assert.Equal("file", replaced.DefaultValue);
                break;

            default:
                Assert.Equal(2, _project.Variables.Globals.Count);
                var renamed = _project.Variables.Globals
                    .Single(variable => variable.Name != "计数");
                Assert.NotEqual("计数", renamed.Name);
                Assert.Equal(renamed.Name, Formula.Word(macro.Steps[0].Parameters[0].Written));
                break;
        }
    }

    [Fact]
    public async Task A_lone_submacro_goes_to_the_public_library()
    {
        var parent = new MacroItem { Name = "父" };
        parent.SubMacros.Add(new MacroItem { Name = "子" });
        var path = Package("library");
        Save(path, [parent], []);

        var planOutcome = await _transfer.PlanImportAsync(_project, path);
        Assert.True(planOutcome.Ok);
        var plan = planOutcome.Value!;
        Assert.Equal(2, plan.Macros.Count);
        Assert.False(plan.Macros[0].IsSub);
        Assert.True(plan.Macros[1].IsSub);

        var reportOutcome = await _transfer.ApplyImportAsync(_project, path, ["子"], []);
        Assert.True(reportOutcome.Ok);
        var report = reportOutcome.Value!;

        Assert.Empty(_project.Macros);
        Assert.Contains("子", report, StringComparison.Ordinal);
        Assert.Contains(PublicMacroLibrary.Load(_library),
            entry => entry.Macro.Name == "子");
    }

    [Fact]
    public async Task An_export_can_be_imported_again()
    {
        _project.AddMacro(Reading("甲", "计数"));
        _project.Variables.Adopt(Global("计数", ValueKind.Number, Formula.Number(2)));

        var planOutcome = _transfer.PlanExport(_project);
        Assert.True(planOutcome.Ok);
        var plan = planOutcome.Value!;
        Assert.EndsWith(MacroPackage.Extension, plan.SuggestedName, StringComparison.Ordinal);
        Assert.Equal("甲", Assert.Single(plan.Macros).Name);

        var path = Package("export");
        var exported = await _transfer.ApplyExportAsync(_project, path, ["甲"]);
        Assert.True(exported.Ok);
        Assert.Equal(path, exported.Value!.Path);

        var fresh = new ProjectWorkspace();
        var incomingOutcome = await _transfer.PlanImportAsync(fresh, path);
        Assert.True(incomingOutcome.Ok);
        var incoming = incomingOutcome.Value!;
        Assert.Contains(incoming.Macros, macro => macro.Name == "甲");
        var variable = Assert.Single(incoming.Globals);
        Assert.Equal("计数", variable.Name);
        Assert.False(variable.Conflicts);

        var imported = await _transfer.ApplyImportAsync(fresh, path, ["甲"], []);
        Assert.True(imported.Ok);

        Assert.Equal("甲", Assert.Single(fresh.Macros).Name);
        Assert.Contains(fresh.Variables.Globals, global => global.Name == "计数");
    }

    [Fact]
    public async Task A_macro_of_only_empty_note_and_log_steps_survives_the_round_trip()
    {
        var macro = new MacroItem { Name = "空动作宏" };
        macro.Steps.Add(new MacroStep { Type = "control.note" });
        macro.Steps.Add(new MacroStep { Type = "control.log" });
        _project.AddMacro(macro);

        var path = Package("empty-steps");
        Assert.True((await _transfer.ApplyExportAsync(_project, path, ["空动作宏"])).Ok);

        var fresh = new ProjectWorkspace();
        var planOutcome = await _transfer.PlanImportAsync(fresh, path);
        Assert.True(planOutcome.Ok);
        var plan = planOutcome.Value!;
        Assert.Equal("空动作宏", Assert.Single(plan.Macros).Name);
        Assert.Equal(2, plan.Macros[0].Steps);

        Assert.True((await _transfer.ApplyImportAsync(fresh, path, ["空动作宏"], [])).Ok);

        var imported = Assert.Single(fresh.Macros);
        Assert.Equal(2, imported.Steps.Count);
    }

    [Fact]
    public async Task Nothing_ticked_is_reported_as_nothing_added()
    {
        var path = Package("nothing");
        Save(path, [new MacroItem { Name = "在的" }], []);

        var result = await _transfer.ApplyImportAsync(_project, path, [], []);

        Assert.True(result.Ok);
        Assert.Equal(Strings.Get("Import.ReportNothing"), result.Value);
        Assert.Empty(_project.Macros);
    }

    [Fact]
    public async Task A_library_package_is_refused_by_project_transfer()
    {
        var path = Package("library");
        PublicMacroLibrary.Save(path,
            [new PublicMacroEntry("public-id", 1, new MacroItem { Name = "shared" })], "test");

        var plan = await _transfer.PlanImportAsync(_project, path);

        Assert.False(plan.Ok);
        Assert.Equal(PackageProblem.LibraryNotProject, plan.Problem);
        Assert.Empty(_project.Macros);
    }

    [Fact]
    public async Task An_unknown_macro_name_is_refused()
    {
        var path = Package("missing");
        Save(path, [new MacroItem { Name = "在的" }], []);

        var result = await _transfer.ApplyImportAsync(_project, path, ["没的"], []);

        Assert.False(result.Ok);
        Assert.Equal(PackageProblem.UnknownMacro, result.Problem);
        Assert.Contains("没的", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_import_report_says_nothing_changed()
    {
        var report = new ImportReport([], [], [], [], [], [], [], []);

        Assert.Equal(Strings.Get("Import.ReportNothing"), PackageTransferService.ReportText(report));
    }

    [Fact]
    public void An_import_report_names_what_arrived_and_what_was_renamed()
    {
        var report = new ImportReport(
            [new MacroItem { Name = "arrived" }],
            [("old", "renamed")],
            ["global"],
            [],
            [],
            [],
            [],
            []);

        var text = PackageTransferService.ReportText(report);

        Assert.Contains("arrived", text, StringComparison.Ordinal);
        Assert.Contains("old", text, StringComparison.Ordinal);
        Assert.Contains("global", text, StringComparison.Ordinal);
    }

    private string Package(string name) => Path.Combine(_folder, name + MacroPackage.Extension);

    private static void Save(string path, IReadOnlyList<MacroItem> macros,
        IReadOnlyList<VariableDefinition> globals)
        => MacroPackage.Save(path, macros, new ProjectTriggers(), globals, "0.0.0");

    private static MacroItem Reading(string name, string variable) => new()
    {
        Name = name,
        Steps =
        {
            new MacroStep
            {
                Type = "test.reads",
                Parameters =
                {
                    new StepParameter
                    {
                        Name = "value",
                        Kind = ActionParameterKind.Text,
                        Written = Formula.Name(variable),
                    },
                },
            },
        },
    };

    private static VariableDefinition Global(string name, ValueKind type, ExpressionNode value)
        => new()
        {
            Name = name,
            Scope = VariableScope.Global,
            Type = type,
            Default = value,
        };
}
