using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Projects;

/// <summary>
/// The read-only project facts the transfer planner needs. It is copied from the live workspace by
/// the shell; the planner never reaches back into a mutable project or a file.
/// </summary>
public sealed record TransferProjectSnapshot(
    IReadOnlyList<MacroItem> Macros,
    ProjectTriggers Triggers,
    IReadOnlyList<VariableDefinition> Globals,
    IReadOnlyList<PublicMacroEntry> Library,
    string AppVersion);

public sealed record TransferPick(MacroItem Macro, bool IsSub);

public sealed record TransferSelection(IReadOnlyList<TransferPick> Picks, string? Missing);

public sealed record ImportDecisionPlan(IReadOnlyList<GlobalDecision> Questions,
    PackageProblem? Problem);

/// <summary>
/// The functional core of transfer: package contents and a project snapshot become the rows,
/// conflicts and package facts the page shows. Nothing here reads a file, mutates a project, or
/// creates an asset.
/// </summary>
public static class TransferPlanner
{
    public static ProjectImportPlan BuildImportPlan(PackageContents contents, string path,
        string fileName, IReadOnlySet<string> projectGlobals)
        => new(
            path,
            fileName,
            Rows(contents.Macros, contents.Triggers),
            [.. contents.Globals.Select(variable => new TransferGlobal(
                variable.Name.Trim(),
                variable.LocalType,
                projectGlobals.Contains(variable.Name.Trim())))],
            new PackagePreview(
                fileName,
                contents.FormatVersion,
                contents.AppVersion,
                contents.SavedUtc,
                contents.HasProject,
                HasLibrary: contents.Library.Count > 0,
                TriggerCount: contents.Triggers.Entries.Count,
                [.. contents.Library.Select(entry => new PreviewLibraryEntry(
                    entry.Id, entry.Macro.Name, entry.Version, "package"))]));

    public static ProjectExportPlan BuildExportPlan(TransferProjectSnapshot project,
        bool includeLibrary)
    {
        var first = project.Macros.FirstOrDefault()?.Name.Trim() ?? string.Empty;
        var suggested = SafeName(first) + MacroPackage.Extension;
        var carry = includeLibrary && project.Library.Count > 0;
        var preview = new PackagePreview(
            suggested,
            MacroPackage.FormatVersion,
            project.AppVersion,
            string.Empty,
            HasProject: true,
            HasLibrary: carry,
            TriggerCount: project.Triggers.Entries.Count,
            carry
                ? [.. project.Library.Select(entry => new PreviewLibraryEntry(
                    entry.Id, entry.Macro.Name, entry.Version, "project"))]
                : []);
        return new ProjectExportPlan(suggested, Rows(project.Macros, project.Triggers), preview);
    }

    /// <summary>The ticked macros, found again by name and in the order the package holds them.</summary>
    public static TransferSelection Select(IReadOnlyList<MacroItem> macros,
        IReadOnlyList<string> names)
    {
        var wanted = names.Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = new List<TransferPick>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in macros)
        {
            Choose(macro, isSub: false, wanted, matched, chosen);
        }

        return new TransferSelection(chosen,
            wanted.FirstOrDefault(name => !matched.Contains(name)));
    }

    private static void Choose(MacroItem macro, bool isSub, IReadOnlySet<string> wanted,
        HashSet<string> matched, List<TransferPick> chosen)
    {
        var name = macro.Name.Trim();
        if (wanted.Contains(name))
        {
            chosen.Add(new TransferPick(macro, isSub));
            matched.Add(name);
            foreach (var sub in macro.SubMacros)
            {
                Cover(sub, matched);
            }

            return;
        }

        // Ticking a macro covers the submacros written inside it: they travel inside it, so they
        // cannot be taken on their own in the same import. Ticking one on its own takes it alone.
        foreach (var sub in macro.SubMacros)
        {
            Choose(sub, isSub: true, wanted, matched, chosen);
        }
    }

    /// <summary>A submacro a ticked macro carries, so its name is not mistaken for a missing one.</summary>
    private static void Cover(MacroItem macro, HashSet<string> matched)
    {
        matched.Add(macro.Name.Trim());
        foreach (var sub in macro.SubMacros)
        {
            Cover(sub, matched);
        }
    }

    /// <summary>
    /// Applies the decisions the page collected to copies of the questions. The input list is left
    /// untouched; a bad action returns a typed problem instead of mutating half of the questions.
    /// </summary>
    public static ImportDecisionPlan ApplyDecisions(IReadOnlyList<GlobalDecision> questions,
        IReadOnlyList<ImportDecision> decisions)
    {
        var planned = questions.Select(question => new GlobalDecision
        {
            Arriving = question.Arriving,
            Existing = question.Existing,
            Choice = question.Choice,
            NewName = question.NewName,
        }).ToList();

        foreach (var decision in decisions)
        {
            var wanted = decision.Name.Trim();
            foreach (var question in planned.Where(question =>
                string.Equals(question.Arriving.Name.Trim(), wanted,
                    StringComparison.OrdinalIgnoreCase)))
            {
                var answer = decision.Action.Trim().ToLowerInvariant() switch
                {
                    ImportActions.Keep => GlobalChoice.UseProject,
                    ImportActions.Overwrite => GlobalChoice.Overwrite,
                    ImportActions.Rename => GlobalChoice.Rename,
                    _ => (GlobalChoice?)null,
                };
                if (answer is null)
                {
                    return new ImportDecisionPlan(planned, PackageProblem.UnknownAction);
                }

                question.Choice = answer.Value;
            }
        }

        return new ImportDecisionPlan(planned, null);
    }

    public static string ReportText(ImportReport report)
    {
        var lines = new List<string>();

        if (report.Added.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportMacros",
                string.Join("、", report.Added.Select(macro => macro.Name))));
        }
        if (report.RenamedMacros.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportRenamed",
                string.Join("、", report.RenamedMacros.Select(pair => pair.Was + " → " + pair.Now))));
        }
        var globals = new List<string>();
        if (report.GlobalsAdded.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalAdded", string.Join("、", report.GlobalsAdded)));
        }
        if (report.GlobalsRenamed.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalRenamed",
                string.Join("、", report.GlobalsRenamed)));
        }
        if (report.GlobalsOverwritten.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalOverwritten",
                string.Join("、", report.GlobalsOverwritten)));
        }
        if (report.GlobalsKept.Count > 0)
        {
            globals.Add(Strings.Format("Import.GlobalKept", string.Join("、", report.GlobalsKept)));
        }
        if (globals.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportGlobals", string.Join("；", globals)));
        }
        if (report.DuplicateTriggers.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportDuplicates",
                string.Join("；", report.DuplicateTriggers)));
        }
        if (report.ScriptsToCheck.Count > 0)
        {
            lines.Add(Strings.Format("Import.ReportScripts", string.Join("；", report.ScriptsToCheck)));
        }

        return lines.Count == 0 ? Strings.Get("Import.ReportNothing") : string.Join("\n", lines);
    }

    /// <summary>
    /// The rows a wizard shows, in the order a tree is read: a macro and then the submacros written
    /// inside it. The order is what lets the page cover them without a parent field: every submacro
    /// after a macro belongs to it until the next macro starts.
    /// </summary>
    internal static IReadOnlyList<TransferMacro> Rows(IReadOnlyList<MacroItem> macros,
        ProjectTriggers triggers)
    {
        var rows = new List<TransferMacro>();
        foreach (var macro in macros)
        {
            AddRow(macro, isSub: false, depth: 0, triggers, rows);
        }

        return rows;
    }

    private static void AddRow(MacroItem macro, bool isSub, int depth, ProjectTriggers triggers,
        List<TransferMacro> rows)
    {
        rows.Add(new TransferMacro(
            macro.Name.Trim(),
            Kind(macro.Name, triggers),
            macro.Steps.Count,
            MacroCheck.DeclarationFaults(macro.Callable).FirstOrDefault() ?? string.Empty,
            isSub,
            depth));

        foreach (var sub in macro.SubMacros)
        {
            AddRow(sub, isSub: true, depth + 1, triggers, rows);
        }
    }

    private static string Kind(string macroName, ProjectTriggers triggers)
    {
        var kinds = triggers.ForMacro(macroName)
            .Select(entry => entry.KindLabel)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return kinds.Count == 0 ? Strings.Get("Trigger.Unset") : string.Join("、", kinds);
    }

    /// <summary>A macro's name with the characters a file name cannot hold taken out.</summary>
    internal static string SafeName(string name)
    {
        var text = name.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(bad, '-');
        }

        text = text.Trim('-', '.', ' ');
        return text.Length == 0 ? "macro" : text;
    }
}
