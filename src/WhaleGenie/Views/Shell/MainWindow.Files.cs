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
        if (_interaction is null)
        {
            return;
        }

        try
        {
            await _interaction.ImportAsync();
        }
        catch (ProjectInteractionException error)
        {
            await ReportAsync(Strings.Get("Package.OpenFailed"), error.Path,
                error.InnerException ?? error);
        }
    }

    private async Task ReportInteractionAsync(string header, string message)
        => await ConfirmDialog.ShowAsync(this, header, message, Strings.Get("Common.Ok"), null,
            false, height: 320);

    /// <summary>
    /// Exports from the file menu: the same window as the card's own menu, with nothing ticked, for
    /// a person who wants to pick several macros rather than start from one of them.
    /// </summary>
    private async void OnExportMacrosClicked(object? sender, RoutedEventArgs e)
        => await ExportMacrosAsync([]);

    /// <summary>
    /// Writes the macro a card was right-clicked on. The window opens with it ticked, because that
    /// is what the click meant; the rest of the list is there for a change of mind.
    /// </summary>
    private async void OnExportMacroClicked(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is Control { DataContext: MacroItem macro })
        {
            await ExportMacrosAsync([macro]);
        }
    }

    private async Task ExportMacrosAsync(IReadOnlyList<MacroItem> preselected)
    {
        if (_interaction is null)
        {
            return;
        }

        try
        {
            await _interaction.ExportAsync(preselected);
        }
        catch (ProjectInteractionException error)
        {
            await ReportAsync(Strings.Get("Main.ExportMacroTitle"), error.Path,
                error.InnerException ?? error);
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
