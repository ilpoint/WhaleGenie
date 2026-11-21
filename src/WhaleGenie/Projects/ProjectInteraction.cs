using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.Views;

namespace WhaleGenie.Projects;

/// <summary>
/// The same import and export the native file menu and the page's project commands both offer:
/// choose a package, decide which macros travel, ask where an export goes, and report what the
/// import did. The window is handed in because both callers already have one; the words come from
/// the one report delegate so the page and the native menu cannot drift apart.
/// </summary>
internal sealed class ProjectInteraction(
    ProjectWorkspace workspace,
    Func<Window?> owner,
    Func<string, string, Task> report)
{
    public async Task ImportAsync()
    {
        if (owner() is not { } window)
        {
            return;
        }

        var path = await PackagePicker.PickAsync(window, Strings.Get("Main.ImportTitle"));
        if (path is null)
        {
            return;
        }

        try
        {
            // Pictures land in this project's folder: the file being imported is not where the
            // macros will be working from. A name already used there is compared by content, so an
            // arriving picture never replaces one another macro looks for.
            var contents = MacroPackage.Load(path, ImageAssets.FolderFor(workspace.CurrentPath));
            var nodes = await MacroChooseWindow.ShowAsync(window, MacroChooseKind.Import,
                contents.Macros, contents.Triggers, workspace.Variables.Globals, contents.Globals,
                fileName: Path.GetFileName(path));
            if (nodes is null || nodes.Count == 0)
            {
                return;
            }

            var chosen = nodes.Select(node => node.Macro).ToList();
            if (nodes.All(node => node.IsSub))
            {
                // Submacros ticked on their own belong in the public library, because that is where
                // something a macro calls lives.
                var added = MacroImport.IntoLibrary(chosen, AppPaths.PublicMacroLibrary);
                await report(Strings.Get("Import.ReportTitle"),
                    Strings.Format("Import.LibraryAdded",
                        string.Join("、", added.Select(entry => entry.Macro.Name))));
                return;
            }

            // What to do about a variable both sides declare is a question, not a default: the wrong
            // answer is a macro that reads somebody else's number without saying so.
            var decisions = MacroImport.Decisions(chosen, contents.Globals, workspace.Variables.Globals);
            if (!await GlobalConflictWindow.ShowAsync(window, decisions, chosen))
            {
                return;
            }

            var reportText = MacroImport.IntoProject(workspace, contents, chosen, decisions);
            await report(Strings.Get("Import.ReportTitle"), ReportText(reportText));
        }
        catch (Exception error)
        {
            throw new ProjectInteractionException(path, error);
        }
    }

    public async Task ExportAsync(IReadOnlyList<MacroItem> preselected)
    {
        if (owner() is not { } window)
        {
            return;
        }

        var nodes = await MacroChooseWindow.ShowAsync(window, MacroChooseKind.Export,
            workspace.Macros, workspace.Triggers, workspace.Variables.Globals,
            preselected: preselected);
        if (nodes is null || nodes.Count == 0)
        {
            return;
        }

        var selection = nodes.Select(node => node.Macro).ToList();
        var path = await PackagePicker.AskWhereAsync(window, Strings.Get("Main.ExportMacroTitle"),
            selection[0].Name);
        if (path is null)
        {
            return;
        }

        try
        {
            // An export is written somewhere else, while the pictures stay beside the project: the
            // folder comes along so the package carries them rather than pointing at this machine.
            MacroPackage.Save(path, MacroBundle.Copies(selection),
                MacroBundle.Take(workspace.Triggers, selection),
                MacroBundle.UsedGlobals(selection, workspace.Variables.Globals), AppVersion.Text,
                ImageAssets.FolderFor(workspace.CurrentPath));
        }
        catch (Exception error)
        {
            throw new ProjectInteractionException(path, error);
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
}

/// <summary>A package operation that failed after its path was known, so the caller can name it.</summary>
internal sealed class ProjectInteractionException(string path, Exception cause)
    : Exception(cause.Message, cause)
{
    public string Path { get; } = path;
}
