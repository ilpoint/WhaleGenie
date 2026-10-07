namespace WhaleGenie.Execution;

/// <summary>
/// The pages the interface sends people to. The help menu sends them to GitHub rather than
/// pretending to have a changelog, a bug tracker or an update mechanism of its own: a page that is
/// really maintained beats a window that is always slightly out of date. The driver WhaleGenie leans
/// on belongs to somebody else, and is handed out the same way.
/// </summary>
public static class ProjectLinks
{
    /// <summary>The project's home page, which is also where "About" points.</summary>
    public const string Home = "https://github.com/ilpoint/WhaleGenie";

    /// <summary>Where a report or a suggestion is filed.</summary>
    public const string Feedback = Home + "/issues";

    /// <summary>What has been released, newest first — the list is the change log.</summary>
    public const string Releases = Home + "/releases";

    /// <summary>Where a new version would be, which is what "check for updates" opens.</summary>
    public const string LatestRelease = Home + "/releases/latest";

    /// <summary>
    /// Where the usbip-win2 driver is fetched from. The newest release is what belongs on the
    /// machine, so the page is named without a version: a number written down here would be wrong
    /// the day after that project released again, and the driver is installed by hand anyway.
    /// </summary>
    public const string DriverDownload = "https://github.com/vadimgrn/usbip-win2/releases/latest";

    /// <summary>
    /// Where the VIIPER server is fetched from, named the same way and for the same reason. Unlike
    /// the driver this one is only unzipped, and WhaleGenie starts it from wherever it was unzipped.
    /// </summary>
    public const string ViiperDownload = "https://github.com/Alia5/VIIPER/releases/latest";
}
