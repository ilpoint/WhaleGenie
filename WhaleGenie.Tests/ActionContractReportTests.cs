using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// What the engine's catalogue and the display catalogue say about each other. The two files answer
/// different questions — what a step does, and what a person reads — and every action needs both
/// halves; a gap used to stop the program at the first one it met.
/// </summary>
public class ActionContractReportTests
{
    /// <summary>One display entry, written the way the check reads one.</summary>
    private static ActionContractDisplay Shown(string key,
        (string Name, string Kind)[]? parameters = null, string[]? returns = null)
        => new(key,
            [.. (parameters ?? []).Select(parameter => new ActionContractParameter(
                parameter.Name, parameter.Kind))],
            [.. returns ?? []]);

    /// <summary>
    /// The files the program ships agree, checked without starting the program: the same answer the
    /// window's first paint would give, asked where a change to either file can be looked at.
    /// </summary>
    [Fact]
    public void The_shipped_catalogues_agree_about_every_action()
    {
        var issues = ActionContract.Check(ActionSemanticsCatalog.All, ActionCatalog.Display);

        Assert.True(issues.Count == 0, string.Join(Environment.NewLine, issues));
    }

    /// <summary>
    /// A broken pair of files is reported in one pass, with every gap located. The point is not that
    /// something is wrong — the old checks said that too — but that the answer names every action,
    /// parameter and result to fix, instead of naming whichever came first.
    /// </summary>
    [Fact]
    public void Every_gap_between_the_two_catalogues_is_reported_at_once()
    {
        // A real action, so the report is about names that exist rather than about made-up ones.
        var delay = ActionSemanticsCatalog.Find("control.delay")!;
        var shown = Shown(delay.Key,
            [("ms", "Number"), ("bogusName", "Number")],
            ["nothing"]);
        var display = new List<ActionContractDisplay>
        {
            shown,
            Shown("control.notAnAction"),
            Shown("control.notAnAction"),
        };

        var issues = ActionContract.Check(ActionSemanticsCatalog.All, display);
        var kinds = issues.Select(issue => issue.Kind).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DuplicateAction", kinds);
        Assert.Contains("OrphanAction", kinds);
        Assert.Contains("OrphanParameter", kinds);
        Assert.Contains("OrphanReturn", kinds);

        // The actions the display file never mentions are missing once each, not folded into one
        // line: the list is a work list, so one line per action is what makes it usable.
        Assert.Contains(issues, issue => issue.Kind == "MissingAction"
            && issue.Action == ActionSemanticsCatalog.All
                .First(action => action.Key != delay.Key).Key);

        // Every line carries what to fix: the kind of gap, the action, and the field inside it when
        // the gap is inside one.
        Assert.All(issues, issue => Assert.Contains(issue.Kind, issue.ToString(), StringComparison.Ordinal));

        // Two entries of one name, and an editor kind the editor does not have, are each their own
        // line: the first says which name to merge, the second says which control cannot be drawn.
        var twice = ActionContract.Check([delay], [Shown(delay.Key, [("ms", "Number")])]);
        Assert.DoesNotContain(twice, issue => issue.Kind == "DuplicateParameter");

        var repeated = ActionContract.Check([delay],
            [Shown(delay.Key, [("ms", "Number"), ("ms", "Number")])]);
        Assert.Contains(repeated, issue => issue.Kind == "DuplicateParameter"
            && issue.Field == "ms");

        var wrongKind = ActionContract.Check([delay], [Shown(delay.Key, [("ms", "Nonsense")])]);
        Assert.Contains(wrongKind, issue => issue.Kind == "UnknownKind" && issue.Field == "ms");
        Assert.All(issues.Where(issue => issue.Kind == "OrphanParameter"),
            issue => Assert.Contains("bogusName", issue.ToString(), StringComparison.Ordinal));
        Assert.All(issues.Where(issue => issue.Kind == "OrphanReturn"),
            issue => Assert.Contains("nothing", issue.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// A parameter the engine has and the display file forgot is named with the action it belongs to
    /// rather than as a count of gaps: "3 parameters are missing somewhere" is not a work item.
    /// </summary>
    [Fact]
    public void A_parameter_the_display_file_forgot_is_named_where_it_belongs()
    {
        var delay = ActionSemanticsCatalog.Find("control.delay")!;
        var issues = ActionContract.Check(ActionSemanticsCatalog.All,
            [Shown(delay.Key, [], [.. delay.Returns.Select(result => result.Key)])]);

        var missing = Assert.Single(issues, issue => issue.Kind == "MissingParameter");
        Assert.Equal(delay.Key, missing.Action);
        Assert.Contains(missing.Field!, delay.Parameters.Select(parameter => parameter.Name));
    }
}
