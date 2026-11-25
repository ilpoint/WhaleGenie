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
            return PackageOutcomes.Success(TransferPlanner.BuildImportPlan(contents, path,
                Path.GetFileName(path), GlobalNames(project)));
        }

        return PackageOutcomes.Failed<ProjectImportPlan>(read.Problem,
            read.Message ?? Strings.Get("Bridge.PackageOpenFailed"), read.Error);
    }

    public Task<PackageOutcome<ProjectImportPlan>> PlanImportAsync(ProjectWorkspace project,
        string path, CancellationToken cancellationToken = default)
        => PreviewAsync(project, path, cancellationToken);

    private static IReadOnlySet<string> GlobalNames(ProjectWorkspace project)
        => project.Variables.Globals.Select(variable => variable.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

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

        var selection = TransferPlanner.Select(contents.Macros, names);
        if (selection.Missing is { } missing)
        {
            return PackageOutcomes.Failed<string>(PackageProblem.UnknownMacro,
                Strings.Format("Bridge.TransferUnknownMacro", missing));
        }

        var picks = selection.Picks;
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
        var plan = TransferPlanner.ApplyDecisions(questions, decisions);
        if (plan.Problem is { } answerProblem)
        {
            return PackageOutcomes.Failed<string>(answerProblem,
                Strings.Get("Bridge.TransferUnknownAction"));
        }

        try
        {
            var report = MacroImport.IntoProject(project, contents, chosen, plan.Questions);
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

            return PackageOutcomes.Success(TransferPlanner.ReportText(report));
        }
        catch (Exception error)
        {
            return PackageOutcomes.Failed<string>(PackageProblem.ImportFailed, error.Message, error);
        }
    }

    /// <summary>Everything the project could export, with a file name to start the save dialog.</summary>
    public PackageOutcome<ProjectExportPlan> PlanExport(ProjectWorkspace project,
        bool includeLibrary = true)
        => PackageOutcomes.Success(TransferPlanner.BuildExportPlan(Snapshot(project),
            includeLibrary));

    private static TransferProjectSnapshot Snapshot(ProjectWorkspace project)
        => new(project.Macros, project.Triggers, project.Variables.Globals, project.Library,
            AppVersion.Text);

    /// <summary>Writes the ticked macros, their triggers, pictures and variables into a package.</summary>
    public async Task<PackageOutcome<ExportResult>> ApplyExportAsync(ProjectWorkspace project,
        string path, IReadOnlyList<string> names, bool includeLibrary = true,
        CancellationToken cancellationToken = default)
    {
        var selection = TransferPlanner.Select(project.Macros, names);
        if (selection.Missing is { } missing)
        {
            return PackageOutcomes.Failed<ExportResult>(PackageProblem.UnknownMacro,
                Strings.Format("Bridge.TransferUnknownMacro", missing));
        }

        var picks = selection.Picks;
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

}
