using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WhaleGenie.Localization;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// The file dialogs a single macro travels through: one to find a <c>.wgm</c>, one to say where it
/// goes. The three places that move a macro in or out of a file — the project list, the editor and
/// the public library — ask the same two questions, so they ask them in one place.
/// </summary>
internal static class MacroFilePicker
{
    /// <summary>
    /// What the dialog calls this kind of file. A project package is a different file with a
    /// different name, and neither dialog offers the other: importing one as the other would only
    /// fail after the choice was made.
    /// </summary>
    internal static FilePickerFileType Type
        => new(Strings.Get("MacroFile.Filter")) { Patterns = [$"*{MacroFile.Extension}"] };

    /// <summary>The file a person picked, or null when they closed the dialog.</summary>
    internal static async Task<string?> PickAsync(TopLevel owner, string title)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [Type],
        });

        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Where a macro is to be written, or null when the dialog was called off.</summary>
    internal static async Task<string?> AskWhereAsync(TopLevel owner, string title, string name)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = SafeName(name) + MacroFile.Extension,
            DefaultExtension = MacroFile.Extension.TrimStart('.'),
            FileTypeChoices = [Type],
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// A macro's name with the characters a file name cannot hold taken out, so the dialog opens
    /// with something it will accept rather than refusing to open at all.
    /// </summary>
    private static string SafeName(string name)
    {
        var text = name.Trim();
        foreach (var bad in System.IO.Path.GetInvalidFileNameChars())
        {
            text = text.Replace(bad, '-');
        }

        text = text.Trim('-', '.', ' ');
        return text.Length == 0 ? "macro" : text;
    }
}
