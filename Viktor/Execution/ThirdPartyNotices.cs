using System;
using System.IO;
using Avalonia.Platform;

namespace Viktor.Execution;

/// <summary>
/// The list of the work inside the program that belongs to somebody else. The file the release
/// carries beside the executable is the same one, so the About box reads the copy packed into the
/// assembly: that copy is always there, wherever the program was started from, and there is no
/// second copy of the list to keep in step.
/// </summary>
public static class ThirdPartyNotices
{
    /// <summary>Where the document sits inside the assembly.</summary>
    private static readonly Uri Location = new(
        $"avares://{typeof(ThirdPartyNotices).Assembly.GetName().Name}/Assets/THIRD-PARTY-NOTICES.md");

    /// <summary>The document, as it is written.</summary>
    public static string Read()
    {
        using var stream = AssetLoader.Open(Location);
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
