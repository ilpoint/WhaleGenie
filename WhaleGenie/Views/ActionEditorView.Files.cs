using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

// Files and pictures: copying a step id, choosing a path, and reading or writing a picture.
public partial class ActionEditorView : UserControl
{
    /// <summary>
    /// Puts the step's name on the clipboard. It is a name people write down — into a condition, a
    /// message, a note to whoever reads the macro next — and typing four characters off a screen is
    /// where they get it wrong.
    /// </summary>
    private async void OnCopyStepId(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AddActionViewModel { HasStepId: true } viewModel)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
        {
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.CreateText(viewModel.StepId));
            await clipboard.SetDataAsync(transfer);
        }
    }

    /// <summary>
    /// Picks the file or folder a path field is about, with the dialog that suits which way the
    /// path is going: reading, writing, or a folder. What comes back is stored the way the engine
    /// will read it — relative when it is inside the macros folder, in full otherwise.
    /// </summary>
    private async void OnBrowsePath(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        var filters = Filter(parameter.Definition.PathFilter,
            parameter.Definition.LocalPathFilterLabel);
        var picked = parameter.Definition.PathIntent switch
        {
            PathIntent.Write => await PickToWrite(parameter, filters),
            PathIntent.Folder => await PickFolder(),
            PathIntent.Files => await PickFiles(parameter),
            _ => await PickToRead(parameter, filters),
        };

        if (picked is { Length: > 0 })
        {
            // A list of files already holds one stored path to a line; a single pick still has to
            // be made relative to the macros folder the way every other path field stores it.
            parameter.Text = parameter.Definition.PathIntent is PathIntent.Files
                ? picked
                : MacroPaths.ForMacro(picked, Devices.Files.BaseFolder);
        }
    }

    /// <summary>The kinds of file a field is about, as dialog filters, or none for any file.</summary>
    private static IReadOnlyList<FilePickerFileType> Filter(string patterns, string label)
    {
        if (patterns.Length == 0)
        {
            return [];
        }

        var wanted = patterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return
        [
            new FilePickerFileType(label.Length > 0 ? label : string.Join(" / ", wanted))
            {
                Patterns = wanted,
            },
            new FilePickerFileType(Strings.Get("Add.AllFiles")) { Patterns = ["*.*"] },
        ];
    }

    private async Task<string> PickToRead(StepParameterViewModel parameter,
        IReadOnlyList<FilePickerFileType> filters)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Format("Add.BrowseOpenTitle", parameter.Definition.LocalLabel),
            AllowMultiple = false,
            FileTypeFilter = filters,
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    private async Task<string> PickToWrite(StepParameterViewModel parameter,
        IReadOnlyList<FilePickerFileType> filters)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Strings.Format("Add.BrowseSaveTitle", parameter.Definition.LocalLabel),
            SuggestedFileName = parameter.Text.Trim(),
            FileTypeChoices = filters,
        });

        return file?.TryGetLocalPath() ?? string.Empty;
    }

    /// <summary>
    /// Picks several files at once and writes them one to a line. A field that holds a list of
    /// files otherwise makes the author repeat the picker — or spell out full paths, which is the
    /// thing the picker exists to avoid.
    /// </summary>
    private async Task<string> PickFiles(StepParameterViewModel parameter)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Format("Add.BrowseOpenTitle", parameter.Definition.LocalLabel),
            AllowMultiple = true,
            FileTypeFilter = Filter(parameter.Definition.PathFilter,
                parameter.Definition.LocalPathFilterLabel),
        });

        return string.Join(Environment.NewLine, files
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .Where(path => path.Length > 0)
            .Select(path => MacroPaths.ForMacro(path, Devices.Files.BaseFolder)));
    }

    private async Task<string> PickFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Strings.Get("Add.BrowseFolderTitle"),
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Gives one result a name nothing else is using, by putting a number after the one it has. It
    /// is what the line under such a name offers, so noticing a clash and doing something
    /// about it is one click rather than a rewrite of the name.
    /// </summary>
    private void OnMakeReturnNameUnique(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ReturnsRowViewModel row })
        {
            row.MakeNameUnique();
        }
    }

    /// <summary>Chooses a picture file for an image parameter.</summary>
    private async void OnBrowseImage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter })
        {
            return;
        }

        if (await PickPictureAsync() is { Length: > 0 } path)
        {
            parameter.Text = path;
        }
    }

    /// <summary>Asks for one picture file, whichever field wanted one.</summary>
    private async Task<string> PickPictureAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Add.BrowseImageTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.Get("Add.ImageFilter"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif"],
                },
            ],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// Drags a rectangle on the screen and keeps it as a picture inside the macro project, which
    /// is how a reference image is normally made.
    /// </summary>
    private async void OnCaptureImage(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: StepParameterViewModel parameter }
            || DataContext is not AddActionViewModel viewModel)
        {
            return;
        }

        try
        {
            RegionPickerWindow.Region? bounds = null;
            if (viewModel.NeedsWindowCoordinates)
            {
                if (viewModel.CoordinateWindowBounds() is not { } target)
                {
                    await ReportTestAsync(Strings.Get("Add.PickCoordinateNeedsWindow"));
                    return;
                }

                bounds = new RegionPickerWindow.Region(target.X, target.Y,
                    target.Width, target.Height);
            }

            if (await RegionPickerWindow.CaptureAsync(HostWindow!, Devices.Screen, Devices.Windows,
                    viewModel.AssetFolder, bounds, Devices.Hook)
                is { Length: > 0 } picture)
            {
                parameter.Text = picture;
            }
        }
        catch (Exception)
        {
            await ConfirmDialog.ShowAsync(HostWindow!, Strings.Get("Add.CaptureImage"),
                Strings.Get("Add.ImageFailed"), Strings.Get("Common.Ok"), showCancel: false);
        }
    }

    /// <summary>Forgets the picture an image parameter points at.</summary>
    private void OnClearImage(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: StepParameterViewModel parameter })
        {
            parameter.Text = string.Empty;
        }
    }
}
