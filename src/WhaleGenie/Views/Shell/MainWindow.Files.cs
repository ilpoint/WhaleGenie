using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Views;

// Projects as files: opening, importing, exporting, saving and the questions a run asks.
public partial class MainWindow : AppWindow
{
    private static FilePickerFileType PackageFileType
        => new(Strings.Get("Package.Filter")) { Patterns = [$"*{MacroPackage.Extension}"] };

    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickPackageAsync(Strings.Get("Main.OpenProjectTitle"));
        if (path is null)
        {
            return;
        }

        var opened = ProjectFiles.Open(viewModel.Workspace, path);
        if (!opened.Ok)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, opened.Error!);
            return;
        }

        LocalSettings.StoreLastProject(path);
    }

    /// <summary>
    /// Imports from a package: the window shows what is inside it, and what was ticked decides
    /// where it goes. Macros join this project; submacros ticked on their own go to the public
    /// library, because that is where something a macro calls belongs.
    /// </summary>
    private async void OnImportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = await PickPackageAsync(Strings.Get("Main.ImportTitle"));
        if (path is null)
        {
            return;
        }

        PackageContents contents;
        try
        {
            // Pictures land in this project's folder: the file being imported is not where the
            // macros will be working from. A name already used there is compared by content, so an
            // arriving picture never replaces one another macro looks for.
            contents = MacroPackage.Load(path, ImageAssets.FolderFor(viewModel.CurrentPath));
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
            return;
        }

        var nodes = await MacroChooseWindow.ShowAsync(this, MacroChooseKind.Import, contents.Macros,
            contents.Triggers, viewModel.Variables.Globals, contents.Globals,
            fileName: Path.GetFileName(path));
        if (nodes is null || nodes.Count == 0)
        {
            return;
        }

        var chosen = nodes.Select(node => node.Macro).ToList();

        try
        {
            if (nodes.All(node => node.IsSub))
            {
                var added = MacroImport.IntoLibrary(chosen, AppPaths.PublicMacroLibrary);
                await ConfirmDialog.ShowAsync(this, Strings.Get("Import.ReportTitle"),
                    Strings.Format("Import.LibraryAdded",
                        string.Join("、", added.Select(entry => entry.Macro.Name))),
                    Strings.Get("Common.Ok"), null, false);
                return;
            }

            // What to do about a variable both sides declare is a question, not a default: the wrong
            // answer is a macro that reads somebody else's number without saying so.
            var decisions = MacroImport.Decisions(chosen, contents.Globals,
                viewModel.Variables.Globals);
            if (!await GlobalConflictWindow.ShowAsync(this, decisions, chosen))
            {
                return;
            }

            var report = MacroImport.IntoProject(viewModel.Workspace, contents, chosen, decisions);
            await ConfirmDialog.ShowAsync(this, Strings.Get("Import.ReportTitle"), ReportText(report),
                Strings.Get("Common.Ok"), null, false, height: 320);
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, error);
        }
    }

    /// <summary>
    /// What the import did, in the order a person checks it: what arrived, what had to be renamed,
    /// what happened to the variables, and what now needs their attention. The web shell's import
    /// answers with the same text, so the two ways in report the same words.
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
                string.Join("、", report.RenamedMacros.Select(pair => $"{pair.Was} → {pair.Now}"))));
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

    /// <summary>
    /// Writes one macro out as a file of its own. The pictures it looks for travel inside the file,
    /// so the person it is handed to does not have to be told what to copy as well.
    /// </summary>
    /// <summary>
    /// Exports from the file menu: the same window as the card's own menu, with nothing ticked, for
    /// a person who wants to pick several macros rather than start from one of them.
    /// </summary>
    private async void OnExportMacrosClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await ExportMacrosAsync(viewModel, []);
        }
    }

    /// <summary>
    /// Writes the macro a card was right-clicked on. The window opens with it ticked, because that
    /// is what the click meant; the rest of the list is there for a change of mind.
    /// </summary>
    private async void OnExportMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro }
            && DataContext is MainViewModel viewModel)
        {
            await ExportMacrosAsync(viewModel, [macro]);
        }
    }

    /// <summary>
    /// Writes the chosen macros into a package: the macros with the submacros inside them, the
    /// global variables they read, the trigger entries that start them and the pictures they look
    /// for. A project's own macros are copied first, because writing a package rewrites the trigger
    /// table of what it is handed.
    /// </summary>
    private async Task ExportMacrosAsync(MainViewModel viewModel, IReadOnlyList<MacroItem> preselected)
    {
        var nodes = await MacroChooseWindow.ShowAsync(this, MacroChooseKind.Export, viewModel.Macros,
            viewModel.Triggers, viewModel.Variables.Globals, preselected: preselected);
        if (nodes is null || nodes.Count == 0)
        {
            return;
        }

        var selection = nodes.Select(node => node.Macro).ToList();

        var path = await PackagePicker.AskWhereAsync(this, Strings.Get("Main.ExportMacroTitle"),
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
                MacroBundle.Take(viewModel.Triggers, selection),
                MacroBundle.UsedGlobals(selection, viewModel.Variables.Globals), AppVersion.Text,
                ImageAssets.FolderFor(viewModel.CurrentPath));
        }
        catch (Exception error)
        {
            await ReportAsync(Strings.Get("Main.ExportMacroTitle"), path, error);
        }
    }

    private async void OnSaveProjectClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.CurrentPath is { Length: > 0 } current)
        {
            Save(viewModel, current);
            return;
        }

        await SaveAsAsync(viewModel);
    }

    private async void OnSaveProjectAsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await SaveAsAsync(viewModel);
        }
    }

    private async void OnReloadCurrentClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CurrentPath: { Length: > 0 } path } viewModel)
        {
            return;
        }

        var opened = ProjectFiles.Open(viewModel.Workspace, path);
        if (!opened.Ok)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), path, opened.Error!);
            return;
        }

        LocalSettings.StoreLastProject(path);
    }

    private void OnOpenLocationClicked(object? sender, RoutedEventArgs e)
        => OpenFolder((DataContext as MainViewModel)?.CurrentFolder);

    private void OnOpenDirectoryClicked(object? sender, RoutedEventArgs e)
        => OpenFolder((DataContext as MainViewModel)?.CurrentFolder
            ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

    /// <summary>The explicit way out, for people who never found the notification-area icon.</summary>
    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        if (Tray is { } tray)
        {
            tray.RequestExit();
            return;
        }

        Close();
    }

    private async void OnCloseAllClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { HasMacros: true } viewModel)
        {
            return;
        }

        var choice = await ConfirmDialog.ShowAsync(this, Strings.Get("Main.CloseAllTitle"),
            Strings.Get("Main.CloseAllMessage"), Strings.Get("Main.CloseAll"));

        if (choice == ConfirmChoice.Primary)
        {
            _triggers?.StopAll();
            viewModel.ClearMacros();
            viewModel.CurrentPath = null;
            RefreshRunning(viewModel);
        }
    }

    private async Task<string?> PickPackageAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [PackageFileType],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>
    /// Writes the project out, asking where it goes when it has no file yet. False when the save
    /// was called off or failed, which is the answer that keeps a program from stopping on a
    /// project it did not manage to write.
    /// </summary>
    private async Task<bool> SaveProjectAsync(MainViewModel viewModel)
        => viewModel.CurrentPath is { Length: > 0 } current
            ? Save(viewModel, current)
            : await SaveAsAsync(viewModel);

    private async Task<bool> SaveAsAsync(MainViewModel viewModel)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Main.SaveProjectTitle"),
            SuggestedFileName = viewModel.CurrentPath is { Length: > 0 } current
                ? Path.GetFileName(current)
                : $"macros{MacroPackage.Extension}",
            DefaultExtension = MacroPackage.Extension.TrimStart('.'),
            FileTypeChoices = [PackageFileType],
            ShowOverwritePrompt = true,
        });

        if (file?.TryGetLocalPath() is { Length: > 0 } path)
        {
            return Save(viewModel, path);
        }

        return false;
    }

    private bool Save(MainViewModel viewModel, string path)
    {
        var saved = ProjectFiles.Save(viewModel.Workspace, path);
        if (!saved.Ok)
        {
            _ = ReportAsync(Strings.Get("Package.SaveFailed"), path, saved.Error!);
            return false;
        }

        LocalSettings.StoreLastProject(path);
        return true;
    }

    /// <summary>Reports a package error without leaving the main window.</summary>
    private async Task ReportAsync(string header, string path, Exception error)
    {
        await ConfirmDialog.ShowAsync(this, header,
            $"{Path.GetFileName(path)}\n{error.Message}", Strings.Get("Common.Ok"), null, false);
    }


    private static void OpenFolder(string? folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
    }
}
