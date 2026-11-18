namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// What a macro wrote as a command line, read as the program and the flags in front of the file
/// the step is about. A program whose path has spaces in it is written in quotes, which is how it
/// would be typed on a command line anyway, so a quote at the front takes everything up to the
/// closing one; otherwise the first word is the program.
/// </summary>
internal static class CommandLine
{
    /// <summary>The program at the front of what was written, and the rest of it.</summary>
    public static (string Program, string Flags) Split(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            if (end > 0)
            {
                return (trimmed[1..end], trimmed[(end + 1)..].Trim());
            }
        }

        var space = trimmed.IndexOf(' ');
        return space < 0
            ? (trimmed, string.Empty)
            : (trimmed[..space], trimmed[(space + 1)..].Trim());
    }
}
