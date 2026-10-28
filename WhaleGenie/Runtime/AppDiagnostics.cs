using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>Writes failures that happen outside a macro step, where no run log can hold them.</summary>
public static class AppDiagnostics
{
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, DateTime> LastWritten =
        new(StringComparer.OrdinalIgnoreCase);
    private const long MaximumBytes = 2 * 1024 * 1024;

    internal static string? FilePathOverride { get; set; }

    public static string FilePath => FilePathOverride ?? Path.Combine(AppPaths.Logs, "runtime.log");

    /// <summary>
    /// Keeps one stage's stack rather than one for every 30 ms timer tick. The process stays alive;
    /// this record is the trail left for the next diagnosis.
    /// </summary>
    public static void Record(string stage, Exception error)
    {
        var now = DateTime.Now;
        if (LastWritten.TryGetValue(stage, out var previous) && now - previous < TimeSpan.FromSeconds(10))
        {
            return;
        }

        LastWritten[stage] = now;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                var path = FilePath;
                if (File.Exists(path) && new FileInfo(path).Length > MaximumBytes)
                {
                    File.Move(path, path + ".1", true);
                }

                var text = new StringBuilder()
                    .AppendLine($"[{now:O}] {stage}")
                    .AppendLine($"thread={Environment.CurrentManagedThreadId} type={error.GetType().FullName}")
                    .AppendLine(error.ToString())
                    .AppendLine()
                    .ToString();
                File.AppendAllText(path, text, new UTF8Encoding(false));
            }
        }
        catch
        {
            // Diagnostics must never become another failure.
        }
    }
}
