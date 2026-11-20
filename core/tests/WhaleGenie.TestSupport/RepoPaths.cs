using IOPath = System.IO.Path;

namespace WhaleGenie.TestSupport;

/// <summary>
/// Where this checkout is, and where the folders a check reads beside the program are filed. A check
/// walks up to the solution file instead of counting levels from its own source: a path built from
/// "the folder above me" stops pointing at the product the day a project is filed under another
/// folder, and the check then reads nothing rather than failing.
/// </summary>
public static class RepoPaths
{
    /// <summary>The checkout root: the folder that holds one of the solution files.</summary>
    public static string Root { get; } = Find();

    /// <summary>A product project's folder or file, given the parts below it.</summary>
    public static string Source(params string[] parts) => Locate("src", parts);

    /// <summary>A suite's folder or file, given the parts below it.</summary>
    public static string Tests(params string[] parts) => Locate("tests", parts);

    /// <summary>A document, given the parts below <c>docs</c>.</summary>
    public static string Docs(params string[] parts) => Join(IOPath.Combine(Root, "docs"), string.Empty, parts);

    /// <summary>Anything else in the checkout, given its parts from the root.</summary>
    public static string Path(params string[] parts) => Join(Root, string.Empty, parts);

    /// <summary>
    /// Where a folder of the library sits. Its own checkout keeps the projects under <c>src</c> and
    /// the suites under <c>tests</c>; the program's checkout vendors the whole library at
    /// <c>core/</c>, so the same relative path has one more level in front of it. Which one exists
    /// is the answer — the two layouts are both real, and a check should read the one it is in.
    /// </summary>
    private static string Locate(string folder, string[] parts)
    {
        var own = Join(Root, folder, parts);
        if (Directory.Exists(own) || File.Exists(own))
        {
            return own;
        }

        return IOPath.Combine(Root, "core", IOPath.Combine([folder, .. parts]));
    }

    private static string Join(string folder, string first, string[] parts)
    {
        var all = new List<string>(parts.Length + 2) { folder };
        if (first.Length > 0)
        {
            all.Add(first);
        }

        all.AddRange(parts);
        return IOPath.Combine([.. all]);
    }

    /// <summary>
    /// Finds the checkout root by walking up until one of the two solution files is there. There
    /// are two because the library also ships as its own checkout: the full solution holds the
    /// editor too, the library one holds only the engine and the Windows projects. Whichever is
    /// present names the root, so the same checks run in both without a second copy of this file.
    /// </summary>
    private static string Find()
    {
        // 局部变量而不是静态字段：静态字段按声明顺序初始化，而 Root 在它们之前就调用了这里，
        // 字段那时还是 null。
        string[] solutions = ["WhaleGenie.slnx", "WhaleGenie.Core.slnx"];

        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (solutions.Any(name => File.Exists(IOPath.Combine(at.FullName, name))))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException(
            string.Join(" or ", solutions) + " was not found above " + AppContext.BaseDirectory
            + ": the checkout root is the folder that holds one of them.");
    }
}
