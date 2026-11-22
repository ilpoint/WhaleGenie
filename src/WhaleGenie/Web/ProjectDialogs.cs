using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
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

        var files = await target.StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickSaveAsync(FilePickerSaveOptions options)
    {
        if (window() is not { } target)
        {
            return null;
        }

        var file = await target.StorageProvider.SaveFilePickerAsync(options);
        return file?.TryGetLocalPath();
    }
}
