using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Projects;

/// <summary>
/// The import and export the page's transfer wizard runs. The file read/write is the package store;
/// extracting rows, finding the ticks and deciding project versus library remains one application
/// use case, so the bridge, MCP and a future caller share it without speaking bridge errors.
/// </summary>
public sealed class PackageTransferService(IPackageStore packages, IPublicLibraryStore publicLibrary)
{
    public async Task<PackageOutcome<ProjectImportPlan>> PreviewAsync(ProjectWorkspace project,
        string path, CancellationToken cancellationToken = default)
    {
        var read = await packages.PreviewAsync(path, cancellationToken).ConfigureAwait(false);
        if (read.Ok && read.Value is { } contents)
        {
            return PackageOutcomes.Success(BuildPlan(project, path, contents));
        }

        return PackageOutcomes.Failed<ProjectImportPlan>(read.Problem,
            read.Message ?? Strings.Get("Bridge.PackageOpenFailed"), read.Error);
    }

    public Task<PackageOutcome<ProjectImportPlan>> PlanImportAsync(ProjectWorkspace project,
        string path, CancellationToken cancellationToken = default)
        => PreviewAsync(project, path, cancellationToken);

    private static ProjectImportPlan BuildPlan(ProjectWorkspace project, string path,
        PackageContents contents)
    {
        var name = Path.GetFileName(path);
        return new ProjectImportPlan(
            path,
            name,
            Rows(contents.Macros, contents.Triggers),
            [.. contents.Globals.Select(variable => new TransferGlobal(
                variable.Name.Trim(),
                variable.LocalType,
                project.Variables.IsGlobal(variable.Name.Trim())))],
            new PackagePreview(
                name,
                contents.FormatVersion,
                contents.AppVersion,
                contents.SavedUtc,
                HasProject: contents.HasProject,
                HasLibrary: contents.Library.Count > 0,
                TriggerCount: contents.Triggers.Entries.Count,
                [.. contents.Library.Select(entry => new PreviewLibraryEntry(
                    entry.Id, entry.Macro.Name, entry.Version, "package"))]));
    }

    /// <summary>
    /// Writes the ticked part of a package into the project, or the public library, and answers the
    /// words the wizard shows. The project is touched on the caller's thread after the package was
    /// read on the store's worker.
    /// </summary>
    public async Task<PackageOutcome<string>> ApplyImportAsync(ProjectWorkspace project, string path,
        IReadOnlyList<string> names, IReadOnlyList<ImportDecision> decisions,
        bool libraryToProject = true, CancellationToken cancellationToken = default)
    {
        var read = await packages.ReadAsync(path, ImageAssets.FolderFor(project.CurrentPath),
            cancellationToken).ConfigureAwait(false);
        if (!read.Ok || read.Value is not { } contents)
        {
            if (read.Problem == PackageProblem.LibraryNotProject)
            {
                return ApplyStandaloneLibrary(project, path, libraryToProject);
            }

            return PackageOutcomes.Failed<string>(read.Problem,
                read.Message ?? Strings.Get("Bridge.PackageOpenFailed"), read.Error);
        }

        var (picks, missing) = Chosen(contents.Macros, names);
        if (missing is not null)
        {
            return PackageOutcomes.Failed<string>(PackageProblem.UnknownMacro,
                Strings.Format("Bridge.TransferUnknownMacro", missing));
        }

        if (picks.Count == 0)
        {
            // Nothing ticked is an ordinary answer in the wizard, not a bad package: the report
            // says that nothing was added.
            return PackageOutcomes.Success(Strings.Get("Import.ReportNothing"));
        }

        // Submacros ticked on their own belong in the public library, because that is where
        // something a macro calls lives. One root macro among them is enough to make this an
        // import into the project instead: the ticks are then one tree, not a pile of pieces.
        if (picks.All(pick => pick.IsSub))
        {
            try
            {
                var added = MacroImport.IntoLibrary([.. picks.Select(pick => pick.Macro)],
                    publicLibrary);
                return PackageOutcomes.Success(Strings.Format("Import.LibraryAdded",
                    string.Join("、", added.Select(entry => entry.Macro.Name))));
            }
            catch (Exception error)
            {
                return PackageOutcomes.Failed<string>(PackageProblem.ImportFailed,
                    error.Message, error);
            }
        }

        var chosen = picks.Select(pick => pick.Macro).ToList();
        var questions = MacroImport.Decisions(chosen, contents.Globals, project.Variables.Globals);
        var answerProblem = Answer(questions, decisions);
        if (answerProblem is not null)
        {
            return PackageOutcomes.Failed<string>(answerProblem.Value,
                Strings.Get("Bridge.TransferUnknownAction"));
        }

        try
        {
            var report = MacroImport.IntoProject(project, contents, chosen, questions);
            if (contents.Library.Count > 0)
            {
                var folder = ImageAssets.FolderFor(project.CurrentPath);
                if (libraryToProject)
                {
                    var added = PublicMacroLibrary.Merge(contents.Library, project.Library,
                        folder, folder);
                    project.MergeLibrary(added);
                }
                else
                {
                    var added = PublicMacroLibrary.Merge(contents.Library,
                        publicLibrary.Load(), PublicMacroLibrary.AssetFolderFor(publicLibrary.Path),
                        folder);
                    publicLibrary.Save([.. publicLibrary.Load(), .. added], AppVersion.Text);
                }
            }

            return PackageOutcomes.Success(ReportText(report));
        }
        catch (Exception error)
        {
            return PackageOutcomes.Failed<string>(PackageProblem.ImportFailed, error.Message, error);
        }
    }

    /// <summary>Everything the project could export, with a file name to start the save dialog.</summary>
    public PackageOutcome<ProjectExportPlan> PlanExport(ProjectWorkspace project,
        bool includeLibrary = true)
    {
        var first = project.Macros.FirstOrDefault()?.Name.Trim() ?? string.Empty;
        var suggested = SafeName(first) + MacroPackage.Extension;
        var carry = includeLibrary && project.Library.Count > 0;
        var preview = new PackagePreview(
            suggested,
            MacroPackage.FormatVersion,
            AppVersion.Text,
            string.Empty,
            HasProject: true,
            HasLibrary: carry,
            TriggerCount: project.Triggers.Entries.Count,
            carry
                ? [.. project.Library.Select(entry => new PreviewLibraryEntry(
                    entry.Id, entry.Macro.Name, entry.Version, "project"))]
                : []);
        return PackageOutcomes.Success(new ProjectExportPlan(
            suggested, Rows(project.Macros, project.Triggers), preview));
    }

    /// <summary>Writes the ticked macros, their triggers, pictures and variables into a package.</summary>
    public async Task<PackageOutcome<ExportResult>> ApplyExportAsync(ProjectWorkspace project,
        string path, IReadOnlyList<string> names, bool includeLibrary = true,
        CancellationToken cancellationToken = default)
    {
        var (picks, missing) = Chosen(project.Macros, names);
        if (missing is not null)
        {
            return PackageOutcomes.Failed<ExportResult>(PackageProblem.UnknownMacro,
                Strings.Format("Bridge.TransferUnknownMacro", missing));
        }

        if (picks.Count == 0)
        {
            return PackageOutcomes.Failed<ExportResult>(PackageProblem.UnknownMacro,
                Strings.Get("Bridge.TransferNeedsMacro"));
        }

        // A submacro that leaves on its own is written as a macro of its own, the same promotion
        // the public library uses: nothing in a file says which half of a tree a macro came from.
        var chosen = picks.Select(pick => pick.IsSub ? MacroImport.Promoted(pick.Macro) : pick.Macro)
            .ToList();
        var write = await packages.WriteAsync(new PackageWriteRequest(
            path,
            MacroBundle.Copies(chosen),
            MacroBundle.Take(project.Triggers, chosen),
            MacroBundle.UsedGlobals(chosen, project.Variables.Globals),
            AppVersion.Text,
            ImageAssets.FolderFor(project.CurrentPath),
            project.Library.Count > 0 && includeLibrary ? project.Library : null),
            cancellationToken).ConfigureAwait(false);
        if (!write.Ok)
        {
            return PackageOutcomes.Failed<ExportResult>(write.Problem,
                write.Message ?? Strings.Get("Bridge.PackageSaveFailed"), write.Error);
        }

        return PackageOutcomes.Success(new ExportResult(path));
    }

    private PackageOutcome<string> ApplyStandaloneLibrary(ProjectWorkspace project, string path,
        bool libraryToProject)
    {
        try
        {
            var entries = publicLibrary.Load(path);
            if (entries.Count == 0)
            {
                return PackageOutcomes.Failed<string>(PackageProblem.InvalidFormat,
                    Strings.Get("Package.NoLibrary"));
            }

            var source = PublicMacroLibrary.AssetFolderFor(path);
            if (libraryToProject)
            {
                var target = ImageAssets.FolderFor(project.CurrentPath);
                var added = PublicMacroLibrary.Merge(entries, project.Library, target, source);
                project.MergeLibrary(added);
                return PackageOutcomes.Success(Strings.Format("Import.LibraryAdded",
                    string.Join("、", added.Select(entry => entry.Macro.Name))));
            }

            var targetLibrary = PublicMacroLibrary.AssetFolderFor(publicLibrary.Path);
            var addedToMachine = PublicMacroLibrary.Merge(entries, publicLibrary.Load(),
                targetLibrary, source);
            publicLibrary.Save([.. publicLibrary.Load(), .. addedToMachine], AppVersion.Text);
            return PackageOutcomes.Success(Strings.Format("Import.LibraryAdded",
                string.Join("、", addedToMachine.Select(entry => entry.Macro.Name))));
        }
        catch (Exception error)
        {
            return PackageOutcomes.Failed<string>(PackageProblem.ImportFailed, error.Message,
                error);
        }
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
    /// One decision for every global conflict the person answered. A name that is not in conflict
    /// was not asked about and is ignored; an answer the wizard does not offer is a bad request
    /// rather than something to guess at.
    /// </summary>
    private static PackageProblem? Answer(IReadOnlyList<GlobalDecision> questions,
        IReadOnlyList<ImportDecision> decisions)
    {
        foreach (var decision in decisions)
        {
            var wanted = decision.Name.Trim();
            var matching = questions.Where(question =>
                string.Equals(question.Arriving.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            foreach (var question in matching)
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
                    return PackageProblem.UnknownAction;
                }

                question.Choice = answer.Value;
            }
        }

        return null;
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
    private static (IReadOnlyList<Pick> Picks, string? Missing) Chosen(
        IReadOnlyList<MacroItem> macros, IReadOnlyList<string> names)
    {
        var wanted = names.Select(name => name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chosen = new List<Pick>();
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in macros)
        {
            Choose(macro, isSub: false, wanted, matched, chosen);
        }

        return (chosen, wanted.FirstOrDefault(name => !matched.Contains(name)));
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
