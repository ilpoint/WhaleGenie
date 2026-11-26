using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using WhaleGenie.Localization;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// The file dialog a macro travels through when it is written. Every place that writes a package
/// asks the same question, so it asks it in one place. There is one kind of file: an export of a
/// few macros is a package with those macros in it, and what the file is meant to become is decided
/// when it is imported.
/// </summary>
internal static class PackagePicker
{
    /// <summary>What the dialog calls this kind of file.</summary>
    internal static FilePickerFileType Type
        => new(Strings.Get("Package.Filter")) { Patterns = [$"*{MacroPackage.Extension}"] };

    /// <summary>Where a macro is to be written, or null when the dialog was called off.</summary>
    internal static async Task<string?> AskWhereAsync(TopLevel owner, string title, string name)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = SafeName(name) + MacroPackage.Extension,
            DefaultExtension = MacroPackage.Extension.TrimStart('.'),
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
