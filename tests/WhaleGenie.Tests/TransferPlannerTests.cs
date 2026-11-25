using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The transfer planner is the functional core of the wizard. These checks build only records and
/// lists: no temp folder, no package, no project, so every row, conflict and shelf is decided before
/// the shell touches a file.
/// </summary>
public class TransferPlannerTests
{
    [Fact]
    public void Selection_covers_a_ticked_macros_children_and_reports_a_missing_name()
    {
        var parent = new MacroItem { Name = "parent" };
        parent.SubMacros.Add(new MacroItem { Name = "child" });
        var other = new MacroItem { Name = "other" };

        var selected = TransferPlanner.Select([parent, other], ["parent"]);
        var missing = TransferPlanner.Select([parent, other], ["nope"]);

        var pick = Assert.Single(selected.Picks);
        Assert.Same(parent, pick.Macro);
        Assert.False(pick.IsSub);
        Assert.Null(selected.Missing);
        Assert.Equal("nope", missing.Missing);
        Assert.Empty(missing.Picks);
    }

    [Fact]
    public void Decisions_are_applied_to_copies_and_an_unknown_action_is_typed()
    {
        var question = new GlobalDecision
        {
            Arriving = Global("count", "数字"),
            Existing = Global("count", "文本"),
        };

        var accepted = TransferPlanner.ApplyDecisions([question],
            [new ImportDecision("count", ImportActions.Keep)]);
        var rejected = TransferPlanner.ApplyDecisions([question],
            [new ImportDecision("count", "make-it-up")]);

        Assert.Null(accepted.Problem);
        Assert.Equal(GlobalChoice.Rename, question.Choice);
        Assert.Equal(GlobalChoice.UseProject, accepted.Questions[0].Choice);
        Assert.Equal(PackageProblem.UnknownAction, rejected.Problem);
        Assert.Equal(GlobalChoice.Rename, rejected.Questions[0].Choice);
    }

    [Fact]
    public void An_import_plan_lists_the_project_shelf_and_marks_conflicts()
    {
        var parent = new MacroItem { Name = "parent" };
        parent.SubMacros.Add(new MacroItem { Name = "child" });
        var contents = new PackageContents
        {
            Macros = [parent],
            Globals = [Global("count", "数字"), Global("fresh", "文本")],
            Triggers = new ProjectTriggers(),
            Library = [new PublicMacroEntry("library-id", 4, new MacroItem { Name = "shared" })],
            HasProject = true,
            FormatVersion = 33,
            AppVersion = "1.2.3",
            SavedUtc = "2026-01-01T00:00:00Z",
        };

        var plan = TransferPlanner.BuildImportPlan(contents, "/tmp/incoming.wgmacro",
            "incoming.wgmacro", Names("count"));

        Assert.Equal("incoming.wgmacro", plan.FileName);
        Assert.Equal(2, plan.Macros.Count);
        Assert.True(plan.Macros[1].IsSub);
        Assert.Equal(1, plan.Macros[1].Depth);
        Assert.Equal("count", Assert.Single(plan.Globals, global => global.Conflicts).Name);
        Assert.True(plan.Preview.HasProject);
        Assert.True(plan.Preview.HasLibrary);
        Assert.Equal("library-id", Assert.Single(plan.Preview.Library).Id);
        Assert.Equal(4, plan.Preview.Library[0].Version);
        Assert.Equal("1.2.3", plan.Preview.AppVersion);
    }

    [Fact]
    public void An_export_plan_can_leave_the_project_library_out()
    {
        var project = new TransferProjectSnapshot(
            [new MacroItem { Name = "only" }],
            new ProjectTriggers(),
            [],
            [new PublicMacroEntry("library-id", 2, new MacroItem { Name = "shared" })],
            "9.9.9");

        var withLibrary = TransferPlanner.BuildExportPlan(project, includeLibrary: true);
        var without = TransferPlanner.BuildExportPlan(project, includeLibrary: false);

        Assert.True(withLibrary.Preview.HasLibrary);
        Assert.Single(withLibrary.Preview.Library);
        Assert.Equal("project", withLibrary.Preview.Library[0].Source);
        Assert.False(without.Preview.HasLibrary);
        Assert.Empty(without.Preview.Library);
        Assert.Equal("only" + MacroPackage.Extension,
            System.IO.Path.GetFileName(withLibrary.SuggestedName));
    }

    private static VariableDefinition Global(string name, string type) => new()
    {
        Name = name,
        Scope = VariableScope.Global,
        Type = type == "数字" ? ValueKind.Number : ValueKind.Text,
    };

    private static IReadOnlySet<string> Names(params string[] names)
        => names.ToHashSet(StringComparer.OrdinalIgnoreCase);
}
