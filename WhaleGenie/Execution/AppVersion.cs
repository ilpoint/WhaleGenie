using System;

namespace WhaleGenie.Execution;

/// <summary>
/// The version the program says it is, read out of the program itself. The release workflow stamps
/// the tag into the assembly, so that is the one place a version is written; a number typed into
/// the program by hand would go on being claimed by every release after the one it was typed for,
/// which is what the badge and the packages did while the about box, which always read the
/// assembly, said something else.
/// </summary>
public static class AppVersion
{
    /// <summary>
    /// The version as the program shows it and writes it down: a leading "v", then the three parts
    /// of the assembly version. A build made by hand rather than by the release workflow carries
    /// the version the build tools fall back on, which is still the truth about that build.
    /// </summary>
    public static string Text { get; } = Read();

    private static string Read()
        => typeof(AppVersion).Assembly.GetName().Version is { } version
            ? $"v{version.Major}.{version.Minor}.{version.Build}"
            : Localization.Strings.Get("Main.AboutUnknownVersion");
}
