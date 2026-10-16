using System;
using System.IO;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The folders the program keeps for itself: the packages a script asks for, and the profiles the
/// browsers it drives sign in on. They sit beside the program, so a copy carried on a stick takes
/// them with it and two copies side by side never share them; a copy installed where it may not
/// write — under Program Files, for instance — keeps them under the user's own folder instead,
/// which is better than a step that can never install anything.
/// </summary>
public static class ScriptFolders
{
    /// <summary>The folder the packages live in, made when it is first needed.</summary>
    public static string Packages { get; } = BesideOrLocal("packages");

    /// <summary>Where the packages installed for Python scripts go, one folder of modules.</summary>
    public static string PythonPackages => Path.Combine(Packages, "python");

    /// <summary>Where the NuGet packages downloaded for C# scripts go, one folder each.</summary>
    public static string NuGetPackages => Path.Combine(Packages, "nuget");

    /// <summary>
    /// Where the browsers a macro drives keep what a site remembers about the person — the sign-in
    /// above all — one folder per browser engine and name, because two engines cannot share one
    /// profile. They follow the program the same way the packages do, so a copy carried about keeps
    /// its own sign-ins and can be taken away with them.
    /// </summary>
    public static string Browsers { get; } = BesideOrLocal("browsers");

    /// <summary>
    /// A folder of the program's own, beside the program when it may be written to and under the
    /// user's own folder when it may not.
    /// </summary>
    private static string BesideOrLocal(string name)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, name);
        if (Writable(beside))
        {
            return beside;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WhaleGenie", name);
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
