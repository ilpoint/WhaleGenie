using System;
using System.IO;
using System.Linq;

namespace WhaleGenie.Tests;

/// <summary>
/// A view's code-behind, read across every part it is split into. A window or a view is one type
/// organised by concern, so a check about what its code says has to look at all of it rather than
/// only the file named after the markup.
/// </summary>
internal static class ViewSource
{
    /// <summary>The parts of <paramref name="fileName"/>'s code-behind, in a stable order.</summary>
    public static string All(string fileName)
    {
        var views = Path.Combine(Repository(), "WhaleGenie", "Views");
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
        return string.Join("\n", Directory.EnumerateFiles(views, stem + "*.cs")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
    }

    /// <summary>The same, for the code-behind beside one markup file given by its full path.</summary>
    public static string Beside(string markupPath)
    {
        var folder = Path.GetDirectoryName(markupPath)!;
        var stem = Path.GetFileNameWithoutExtension(markupPath);
        return string.Join("\n", Directory.EnumerateFiles(folder, stem + "*.cs")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
    }

    /// <summary>
    /// A view's markup, read together with the parts it was split into: a window keeps its layout
    /// in the file named after it and may keep its styles or resources beside it.
    /// </summary>
    public static string Markup(string fileName)
    {
        var views = Path.Combine(Repository(), "WhaleGenie", "Views");
        var stem = Path.GetFileNameWithoutExtension(fileName);
        return string.Join("\n", Directory.EnumerateFiles(views, stem + "*.axaml")
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
