using System.Text.RegularExpressions;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// What the program says its own version is. The release workflow stamps the tag into the
/// assembly, so that is the one place a version is written; the badge in the corner of the window
/// and the packages wrote out a number of their own and went on claiming it release after release,
/// while the about box, which has always read the assembly, said something else.
/// </summary>
public class AppVersionTests
{
    [Fact]
    public void The_badge_says_what_the_about_box_says()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version
            ?? throw new InvalidOperationException("the program's own assembly carries no version");

        Assert.Equal($"v{version.Major}.{version.Minor}.{version.Build}", new MainViewModel().Version);
    }

    [Fact]
    public void No_source_file_of_the_program_writes_a_version_of_its_own()
    {
        // A version typed into the program is right for exactly one release and quietly wrong for
        // every one after it. Reading it out of the assembly is the only way it may appear here,
        // and that includes the notes: what a release was belongs in the commit that made it.
        var offenders = new List<string>();

        foreach (var file in Sources())
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (WrittenVersion.IsMatch(line))
                {
                    offenders.Add($"{Path.GetRelativePath(Repository(), file)}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "these lines write a version the release tag does not decide: " + string.Join("; ", offenders));
    }

    /// <summary>A version written out as a number, which is what a tag turns into.</summary>
    private static readonly Regex WrittenVersion = new(@"\d+\.\d+\.\d+");

    /// <summary>Every source file of the program the user runs.</summary>
    private static IEnumerable<string> Sources()
        => Directory.EnumerateFiles(Path.Combine(Repository(), "WhaleGenie"), "*.*",
                SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                           || file.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal)
                           && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                               StringComparison.Ordinal));

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
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
