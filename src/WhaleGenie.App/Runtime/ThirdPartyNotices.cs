using System;
using System.IO;
using System.Linq;

namespace WhaleGenie.Runtime;

/// <summary>
/// The list of the work inside the program that belongs to somebody else. The file the release
/// carries beside the executable is the same one, so the About box reads the copy embedded in the
/// assembly: that copy is always there, wherever the program was started from, and there is no
/// second copy of the list to keep in step.
/// </summary>
public static class ThirdPartyNotices
{
    /// <summary>The document, as it is written.</summary>
    public static string Read()
    {
        var assembly = typeof(ThirdPartyNotices).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(candidate => candidate.EndsWith("THIRD-PARTY-NOTICES.md",
                StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("the third-party notice is missing");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
