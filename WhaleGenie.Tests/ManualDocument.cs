using System.Runtime.CompilerServices;

namespace WhaleGenie.Tests;

/// <summary>
/// Reads the manual as one document even though it is kept as a directory of chapters. The tests
/// that reconcile a table or a failure key with the rest of the product should not depend on which
/// chapter that table lives in.
/// </summary>
internal static class ManualDocument
{
    public static IReadOnlyList<string> Files()
        => Directory.GetFiles(Folder(), "*.md", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static string AllText()
        => string.Join("\n\n", Files().Select(File.ReadAllText));

    public static string[] AllLines()
        => Files().SelectMany(File.ReadAllLines).ToArray();

    private static string Folder([CallerFilePath] string here = "")
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(here))!, "..", "docs", "manual");
}
