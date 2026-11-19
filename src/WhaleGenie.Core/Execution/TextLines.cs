namespace WhaleGenie.Core.Execution;

/// <summary>
/// Text a step wrote one entry to a line. The line endings are whichever the person's editor wrote,
/// so a macro moved between Windows and a file made elsewhere reads the same entries.
/// </summary>
internal static class TextLines
{
    /// <summary>The entries of a field written one to a line, whichever line ending was used.</summary>
    public static string[] Split(string text)
        => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
