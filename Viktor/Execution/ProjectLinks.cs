namespace Viktor.Execution;

/// <summary>
/// The project's own pages. The help menu sends people to GitHub rather than pretending to
/// have a changelog, a bug tracker or an update mechanism of its own: a page that is really
/// maintained beats a window that is always slightly out of date.
/// </summary>
public static class ProjectLinks
{
    /// <summary>The project's home page, which is also where "About" points.</summary>
    public const string Home = "https://github.com/ilpoint/Viktor";

    /// <summary>Where a report or a suggestion is filed.</summary>
    public const string Feedback = Home + "/issues";

    /// <summary>What has been released, newest first — the list is the change log.</summary>
    public const string Releases = Home + "/releases";

    /// <summary>Where a new version would be, which is what "check for updates" opens.</summary>
    public const string LatestRelease = Home + "/releases/latest";
}
