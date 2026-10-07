using System;
using System.IO;

namespace WhaleGenie.Execution;

/// <summary>
/// Where WhaleGenie keeps the things that belong to the copy of it that is running. They sit beside
/// the program rather than somewhere in the user's profile, so a copy carried on a stick takes
/// its own choices and its own pictures with it, and two copies side by side never share either.
/// </summary>
/// <remarks>
/// A copy installed where it may not write — under Program Files, for instance — simply keeps its
/// defaults and writes no pictures, which is better than refusing to start.
/// </remarks>
public static class AppPaths
{
    /// <summary>The folder the program itself is in.</summary>
    public static string Root { get; } = AppContext.BaseDirectory;

    /// <summary>The choices made in the settings window.</summary>
    public static string Settings { get; } = Path.Combine(Root, "settings.json");

    /// <summary>Pictures of the runs that stopped on a failure.</summary>
    public static string Logs { get; } = Path.Combine(Root, "logs");
}
