using System;
using System.IO;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Where the packages a script asks for are kept. They sit beside the program, so a copy carried on
/// a stick takes its packages with it and two copies side by side never share them; a copy installed
/// where it may not write — under Program Files, for instance — keeps them under the user's own
/// folder instead, which is better than a step that can never install anything.
/// </summary>
public static class ScriptFolders
{
    /// <summary>The folder the packages live in, made when it is first needed.</summary>
    public static string Packages { get; } = Choose();

    /// <summary>Where the packages installed for Python scripts go, one folder of modules.</summary>
    public static string PythonPackages => Path.Combine(Packages, "python");

    /// <summary>Where the NuGet packages downloaded for C# scripts go, one folder each.</summary>
    public static string NuGetPackages => Path.Combine(Packages, "nuget");

    private static string Choose()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "packages");
        if (Writable(beside))
        {
            return beside;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhaleGenie", "packages");
    }

    /// <summary>
    /// Whether a folder can be written in, asked by writing in it: a permissions question is
    /// answered by trying rather than by reading, because the answer depends on more than the
    /// folder's own attributes.
    /// </summary>
    private static bool Writable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $"write-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception failure)
            when (failure is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
