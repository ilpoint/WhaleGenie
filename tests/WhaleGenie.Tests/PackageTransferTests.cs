using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    private readonly ProjectWorkspace _project = new();

    public PackageTransferTests()
    {
        Directory.CreateDirectory(_folder);
        _library = Path.Combine(_folder, "public-macros.wgmacro");
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, true);
        }
    }

    [Fact]
    public void An_empty_project_takes_what_the_package_holds()
    {
        var path = Package("from");
        Save(path, [new MacroItem { Name = "远程" }], []);

        var plan = PackageTransfer.PlanImport(_project, path);
        var row = Assert.Single(plan.Macros);
        Assert.Equal(path, plan.Source);
        Assert.Equal("from.wgmacro", plan.FileName);
        Assert.Equal("远程", row.Name);
        Assert.False(row.IsSub);
        Assert.Equal(0, row.Steps);
        Assert.Empty(row.Problem);
        Assert.Empty(plan.Globals);

        var report = PackageTransfer.ApplyImport(_project, path, ["远程"], [], _library);

        Assert.Contains("远程", report, StringComparison.Ordinal);
        Assert.Equal("远程", Assert.Single(_project.Macros).Name);
    }

    [Theory]
    [InlineData("keep")]
    [InlineData("overwrite")]
    [InlineData("rename")]
    public void A_global_both_sides_declare_is_settled_by_the_answer(string action)
    {
        _project.Variables.Adopt(Global("计数", ValueKind.Number, Formula.Number(1)));
        var path = Package("conflict");
        Save(path, [Reading("远程", "计数")],
            [Global("计数", ValueKind.Text, Formula.Text("file"))]);

        var plan = PackageTransfer.PlanImport(_project, path);
        var arriving = Assert.Single(plan.Globals);
        Assert.True(arriving.Conflicts);

        PackageTransfer.ApplyImport(_project, path,
            ["远程"], [new ImportDecision("计数", action)], _library);

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
    public void A_lone_submacro_goes_to_the_public_library()
    {
        var parent = new MacroItem { Name = "父" };
        parent.SubMacros.Add(new MacroItem { Name = "子" });
        var path = Package("library");
        Save(path, [parent], []);

        var plan = PackageTransfer.PlanImport(_project, path);
        Assert.Equal(2, plan.Macros.Count);
        Assert.False(plan.Macros[0].IsSub);
        Assert.True(plan.Macros[1].IsSub);

        var report = PackageTransfer.ApplyImport(_project, path, ["子"], [], _library);

        Assert.Empty(_project.Macros);
        Assert.Contains("子", report, StringComparison.Ordinal);
        Assert.Contains(PublicMacroLibrary.Load(_library),
            entry => entry.Macro.Name == "子");
    }

    [Fact]
    public void An_export_can_be_imported_again()
    {
        _project.AddMacro(Reading("甲", "计数"));
        _project.Variables.Adopt(Global("计数", ValueKind.Number, Formula.Number(2)));

        var plan = PackageTransfer.PlanExport(_project);
        Assert.EndsWith(MacroPackage.Extension, plan.SuggestedName, StringComparison.Ordinal);
        Assert.Equal("甲", Assert.Single(plan.Macros).Name);

        var path = Package("export");
        var result = PackageTransfer.ApplyExport(_project, path, ["甲"]);
        Assert.Equal(path, result.Path);

        var fresh = new ProjectWorkspace();
        var incoming = PackageTransfer.PlanImport(fresh, path);
        Assert.Contains(incoming.Macros, macro => macro.Name == "甲");
        var variable = Assert.Single(incoming.Globals);
        Assert.Equal("计数", variable.Name);
        Assert.False(variable.Conflicts);

        PackageTransfer.ApplyImport(fresh, path, ["甲"], [], _library);

        Assert.Equal("甲", Assert.Single(fresh.Macros).Name);
        Assert.Contains(fresh.Variables.Globals, global => global.Name == "计数");
    }

    [Fact]
    public void An_unknown_macro_name_is_refused()
    {
        var path = Package("missing");
        Save(path, [new MacroItem { Name = "在的" }], []);

        var error = Assert.Throws<BridgeFailureException>(
            () => PackageTransfer.ApplyImport(_project, path, ["没的"], [], _library));

        Assert.Equal(BridgeErrors.BadArgument, error.Code);
        Assert.Contains("没的", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_import_report_says_nothing_changed()
    {
        var report = new ImportReport([], [], [], [], [], [], [], []);

        Assert.Equal(Strings.Get("Import.ReportNothing"), PackageTransfer.ReportText(report));
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

        var text = PackageTransfer.ReportText(report);

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
