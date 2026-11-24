using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Projects;

/// <summary>
/// The import and export the page's transfer wizard runs: what a package holds before anything is
/// written, and the one write that carries the ticks out.
/// </summary>
/// <remarks>
/// The two directions are the same question — a tree of macros and a tick — so they share the walk
/// that lists them and the one that finds the ticks again. What differs is the write: an import
/// either adds macros to the project or puts lone submacros in the public library, and an export
/// writes a package of the chosen macros and the variables they read. No window and no picker is
/// involved here, which is what lets the whole path be a use case rather than a click.
/// </remarks>
public static class PackageTransfer
{
    /// <summary>What a package file would bring, with the conflicts the project already has.</summary>
    public static ProjectImportPlan PlanImport(ProjectWorkspace project, string path)
    {
        var contents = Read(project, path);
        return new ProjectImportPlan(
            path,
            Path.GetFileName(path),
            Rows(contents.Macros, contents.Triggers),
            [.. contents.Globals.Select(variable => new TransferGlobal(
                variable.Name.Trim(),
                variable.LocalType,
                project.Variables.IsGlobal(variable.Name.Trim())))]);
    }

    /// <summary>
    /// Writes the ticked part of a package into the project, or the public library, and answers the
    /// words the wizard shows.
    /// </summary>
    public static string ApplyImport(ProjectWorkspace project, string path,
        IReadOnlyList<string> names, IReadOnlyList<ImportDecision> decisions,
        string? publicLibrary = null)
    {
        var contents = Read(project, path);
        var picks = Chosen(contents.Macros, names);
        if (picks.Count == 0)
        {
            return Strings.Get("Import.ReportNothing");
        }

        // Submacros ticked on their own belong in the public library, because that is where
        // something a macro calls lives. One root macro among them is enough to make this an
        // import into the project instead: the ticks are then one tree, not a pile of pieces.
        if (picks.All(pick => pick.IsSub))
        {
            var added = MacroImport.IntoLibrary([.. picks.Select(pick => pick.Macro)],
                publicLibrary ?? AppPaths.PublicMacroLibrary);
            return Strings.Format("Import.LibraryAdded",
                string.Join("、", added.Select(entry => entry.Macro.Name)));
        }

        var chosen = picks.Select(pick => pick.Macro).ToList();
        var questions = MacroImport.Decisions(chosen, contents.Globals, project.Variables.Globals);
        Answer(questions, decisions);
        return ReportText(MacroImport.IntoProject(project, contents, chosen, questions));
    }

    /// <summary>Everything the project could export, with a file name to start the save dialog.</summary>
    public static ProjectExportPlan PlanExport(ProjectWorkspace project)
    {
        var first = project.Macros.FirstOrDefault()?.Name.Trim() ?? string.Empty;
        return new ProjectExportPlan(SafeName(first) + MacroPackage.Extension,
            Rows(project.Macros, project.Triggers));
    }

    /// <summary>Writes the ticked macros, their triggers, pictures and variables into a package.</summary>
    public static ExportResult ApplyExport(ProjectWorkspace project, string path,
        IReadOnlyList<string> names)
    {
        var picks = Chosen(project.Macros, names);
        if (picks.Count == 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.TransferNeedsMacro"));
        }

        // A submacro that leaves on its own is written as a macro of its own, the same promotion
        // the public library uses: nothing in a file says which half of a tree a macro came from.
        var chosen = picks.Select(pick => pick.IsSub ? MacroImport.Promoted(pick.Macro) : pick.Macro)
            .ToList();
        MacroPackage.Save(path, MacroBundle.Copies(chosen),
            MacroBundle.Take(project.Triggers, chosen),
            MacroBundle.UsedGlobals(chosen, project.Variables.Globals), AppVersion.Text,
            ImageAssets.FolderFor(project.CurrentPath));
        return new ExportResult(path);
    }

    /// <summary>
    /// What the import did, in the order a person checks it: what arrived, what had to be renamed,
    /// what happened to the variables, and what now needs their attention.
    /// </summary>
    internal static string ReportText(ImportReport report)
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
            globals.Add(Strings.Format("Import.GlobalRenamed", string.Join("、", report.GlobalsRenamed)));
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

    private static PackageContents Read(ProjectWorkspace project, string path)
        => MacroPackage.Load(path, ImageAssets.FolderFor(project.CurrentPath));

    /// <summary>
    /// One decision for every global conflict the person answered. A name that is not in conflict
    /// was not asked about and is ignored; an answer the wizard does not offer is a bad request
    /// rather than something to guess at.
    /// </summary>
    private static void Answer(IReadOnlyList<GlobalDecision> questions,
        IReadOnlyList<ImportDecision> decisions)
    {
        foreach (var decision in decisions)
        {
            var wanted = decision.Name.Trim();
            var matching = questions.Where(question =>
                string.Equals(question.Arriving.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            foreach (var question in matching)
            {
                question.Choice = decision.Action.Trim().ToLowerInvariant() switch
                {
                    ImportActions.Keep => GlobalChoice.UseProject,
                    ImportActions.Overwrite => GlobalChoice.Overwrite,
                    ImportActions.Rename => GlobalChoice.Rename,
                    _ => throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Bridge.TransferUnknownAction")),
                };
            }
        }
    }

    /// <summary>
    /// The rows a wizard shows, in the order a tree is read: a macro and then the submacros written
    /// inside it. The order is what lets the page cover them without a parent field: every submacro
    /// after a macro belongs to it until the next macro starts.
    /// </summary>
    private static IReadOnlyList<TransferMacro> Rows(IReadOnlyList<MacroItem> macros,
        ProjectTriggers triggers)
    {
        var rows = new List<TransferMacro>();
        foreach (var macro in macros)
        {
            AddRow(macro, isSub: false, triggers, rows);
        }

        return rows;
    }

    private static void AddRow(MacroItem macro, bool isSub, ProjectTriggers triggers,
        List<TransferMacro> rows)
    {
        rows.Add(new TransferMacro(
            macro.Name.Trim(),
            Kind(macro.Name, triggers),
            macro.Steps.Count,
            MacroCheck.DeclarationFaults(macro.Callable).FirstOrDefault() ?? string.Empty,
            isSub));

        foreach (var sub in macro.SubMacros)
        {
            AddRow(sub, isSub: true, triggers, rows);
        }
    }

    /// <summary>The trigger kinds the macro answers to, as the editor's picker says them.</summary>
    private static string Kind(string macroName, ProjectTriggers triggers)
    {
        var kinds = triggers.ForMacro(macroName)
            .Select(entry => entry.KindLabel)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return kinds.Count == 0 ? Strings.Get("Trigger.Unset") : string.Join("、", kinds);
    }

    /// <summary>The ticked macros, found again by name and in the order the package holds them.</summary>
    private static IReadOnlyList<Pick> Chosen(IReadOnlyList<MacroItem> macros,
        IReadOnlyList<string> names)
    {
        var wanted = names.Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = new List<Pick>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in macros)
        {
            Choose(macro, isSub: false, wanted, matched, chosen);
        }

        var missing = wanted.FirstOrDefault(name => !matched.Contains(name));
        if (missing is not null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Format("Bridge.TransferUnknownMacro", missing));
        }

        return chosen;
    }

    private static void Choose(MacroItem macro, bool isSub, IReadOnlySet<string> wanted,
        HashSet<string> matched, List<Pick> chosen)
    {
        var name = macro.Name.Trim();
        if (wanted.Contains(name))
        {
            chosen.Add(new Pick(macro, isSub));
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

    /// <summary>A macro's name with the characters a file name cannot hold taken out.</summary>
    private static string SafeName(string name)
    {
        var text = name.Trim();
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            text = text.Replace(bad, '-');
        }

        text = text.Trim('-', '.', ' ');
        return text.Length == 0 ? "macro" : text;
    }

    private readonly record struct Pick(MacroItem Macro, bool IsSub);
}
