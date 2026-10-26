using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Range;

/// <summary>Which edge of a range a run crossed.</summary>
public enum RangeEdge
{
    /// <summary>A step the range never runs, such as a script or a program of its own.</summary>
    Action,

    /// <summary>A place outside the folder a range may write to.</summary>
    Path,
}

/// <summary>Where a range window is, in screen pixels, and which handle it has.</summary>
public sealed record RangeBounds(long Handle, int X, int Y, int Width, int Height);

/// <summary>
/// What a run is allowed to touch while the range window is open. The range is only worth calling a
/// range if the edges are enforced: a client that can still click anywhere, move any window or write
/// any file has a target, not a sandbox. Everything here answers one question and says why not, so a
/// refusal can name the edge that was crossed instead of "not allowed".
/// </summary>
public static class RangePolicy
{
    /// <summary>Where a range run may write: made once, emptied by nobody, outside the project.</summary>
    public static string Folder { get; } = Path.Combine(Path.GetTempPath(), "whalegenie-range");

    /// <summary>The folder is made when it is first needed, so a range can write without a setup step.</summary>
    public static string Ready()
    {
        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A machine that will not make the folder still gets an answer: the writes will fail
            // with the step's own failure key, which is what the person would see too.
        }

        return Folder;
    }

    /// <summary>The actions a range run refuses outright, whatever their parameters say.</summary>
    private static readonly HashSet<string> Forbidden = new(StringComparer.Ordinal)
    {
        // Anything that is a program of its own: a range cannot promise what it does.
        "script.run", "script.python", "script.pip", "script.csharp", "script.nuget", "script.ahk",

        // Anything that ends something or turns the machine off.
        "process.kill", "system.power", "command.run",
    };

    /// <summary>The parameters of a file action that name a place to write.</summary>
    private static readonly string[] Paths = ["path", "folder", "from", "to", "logLine", "fileName"];

    /// <summary>How long one run may take here: a range is for trying, not for waiting out a night.</summary>
    public const int LongestRunMs = 60_000;

    /// <summary>True when a point is inside the window a range run opened.</summary>
    public static bool Inside(int x, int y, int left, int top, int width, int height)
        => x >= left && x < left + width && y >= top && y < top + height;

    /// <summary>
    /// Why a run may not go ahead, or null when every step is one a range can promise. The reasons
    /// are the edges themselves, so a client learns what the range is rather than only that it was
    /// refused.
    /// </summary>
    public static (RangeEdge Edge, string Why)? Judge(IReadOnlyList<ExecutableStep> steps)
    {
        foreach (var step in Every(steps))
        {
            if (Forbidden.Contains(step.Type))
            {
                return (RangeEdge.Action,
                    $"\"{step.Type}\" is not something a range run does: it starts a program of its own "
                    + "or ends something, and a range cannot promise what that would do.");
            }

            if (!step.Type.StartsWith("file.", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var name in Paths)
            {
                var written = step.Text(name).Trim();
                if (written.Length == 0 || written.StartsWith('$') || written.StartsWith("${", StringComparison.Ordinal))
                {
                    // A path a macro works out at run time cannot be judged here; it is judged by the
                    // step's own failure when it runs, which is the same answer the person would get.
                    continue;
                }

                if (!Under(written))
                {
                    return (RangeEdge.Path,
                        $"\"{step.Type}\" writes \"{written}\", which is outside the range folder {Folder}.");
                }
            }
        }

        return null;
    }

    /// <summary>Whether a path is inside the folder a range run may write to.</summary>
    public static bool Under(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(Folder);
            return full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), root,
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException
            or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Every step, including the ones inside other steps.</summary>
    private static IEnumerable<ExecutableStep> Every(IReadOnlyList<ExecutableStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var parameter in step.Parameters)
            {
                foreach (var child in Every(parameter.Steps))
                {
                    yield return child;
                }
            }
        }
    }
}
