using System;
using System.IO;
using System.Linq;

namespace WhaleGenie.Tests.Support;

/// <summary>
/// The windows as files: where one lives, and its markup or code-behind read across every part it
/// is split into. A window is one type organised by concern, so a check about what it says has to
/// look at all of it rather than only the file named after the markup — and a check that hard-codes
/// the folder it was filed under breaks the day the file moves for a reason of its own.
/// </summary>
internal static class ViewSource
{
    /// <summary>The parts of <paramref name="fileName"/>'s code-behind, in a stable order.</summary>
    public static string All(string fileName)
    {
        var views = Path.Combine(Repository(), "WhaleGenie", "Views");
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
        return string.Join("\n", Directory.EnumerateFiles(views, stem + "*.cs", SearchOption.AllDirectories)
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
        return string.Join("\n", Directory.EnumerateFiles(views, stem + "*.axaml", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(File.ReadAllText));
    }

    /// <summary>
    /// Several markup files read as one, for a window whose pages were split into views of their
    /// own: a check about what the window offers has to look at the pages as well as the frame.
    /// </summary>
    public static string MarkupAnd(params string[] fileNames) =>
        string.Join("\n", fileNames.Select(Markup));

    /// <summary>
    /// Where one view file lives. The windows are filed by what they belong to, and a check that
    /// hard-codes the folder breaks the day a file moves for a reason that has nothing to do with
    /// it. The answer must be unique: two files with one name would make every check that reads
    /// "the" view read a coin toss.
    /// </summary>
    public static string PathOf(string fileName)
    {
        var views = Path.Combine(Repository(), "WhaleGenie", "Views");
        var found = Directory.EnumerateFiles(views, fileName, SearchOption.AllDirectories).ToList();
        return found.Count == 1
            ? found[0]
            : throw new InvalidOperationException(
                $"{found.Count} files named {fileName} under {views}: {string.Join(", ", found)}");
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
