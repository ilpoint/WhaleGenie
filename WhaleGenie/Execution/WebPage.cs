using System.Diagnostics;

namespace WhaleGenie.Execution;

/// <summary>
/// Hands a web address to whatever this machine opens web addresses with. WhaleGenie never fetches a
/// page itself: every link is a page for the person to read, and what to do with it is theirs.
/// </summary>
public static class WebPage
{
    /// <summary>Opens <paramref name="url"/> in the machine's browser.</summary>
    public static void Open(string url)
        => Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
}
