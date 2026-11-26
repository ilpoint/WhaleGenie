using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.Web;

/// <summary>The shell window's file pickers, behind the bridge's port.</summary>
internal sealed class ProjectDialogs(Func<Window?> window) : IFileDialogs
{
    public Task<string?> OpenPackageAsync()
        => PickAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Main.OpenProjectTitle"),
            AllowMultiple = false,
            FileTypeFilter = [PackageFileType()],
        });

    public Task<string?> SavePackageAsync(string suggestedName)
        => PickSaveAsync(new FilePickerSaveOptions
        {
            Title = Strings.Get("Main.SaveProjectTitle"),
            SuggestedFileName = suggestedName,
            DefaultExtension = MacroPackage.Extension.TrimStart('.'),
            FileTypeChoices = [PackageFileType()],
            ShowOverwritePrompt = true,
        });

    public Task<string?> OpenImageAsync()
        => PickAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Menu.ImageFiles"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.Get("Add.ImageFilter"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"],
                },
            ],
        });

    private static FilePickerFileType PackageFileType()
        => new(Strings.Get("Package.Filter")) { Patterns = [$"*{MacroPackage.Extension}"] };

    private async Task<string?> PickAsync(FilePickerOpenOptions options)
    {
        if (window() is not { } target)
        {
            return null;
        }

        AppDiagnostics.Note("dialogs.openPackage",
            $"ui={Dispatcher.UIThread.CheckAccess()} visible={target.IsVisible}");
        try
        {
            // A file picker is a window operation and must run on the interface thread. The bridge
            // normally calls handlers there, but the WebView's message event is the one place
            // where that is an implementation detail; making the picker check keeps a native
            // dialog from being asked to open from a background continuation.
            return await OnUiThread(async () =>
            {
                var files = await target.StorageProvider.OpenFilePickerAsync(options);
                return files.Count > 0 ? files[0].TryGetLocalPath() : null;
            });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            AppDiagnostics.Record("dialogs.openPackage", error);
            return null;
        }
    }

    private async Task<string?> PickSaveAsync(FilePickerSaveOptions options)
    {
        if (window() is not { } target)
        {
            return null;
        }

        try
        {
            return await OnUiThread(async () =>
            {
                var file = await target.StorageProvider.SaveFilePickerAsync(options);
                return file?.TryGetLocalPath();
            });
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            AppDiagnostics.Record("dialogs.savePackage", error);
            return null;
        }
    }

    private static Task<T> OnUiThread<T>(Func<Task<T>> work)
        => Dispatcher.UIThread.CheckAccess() ? work() : Dispatcher.UIThread.InvokeAsync(work);
}
