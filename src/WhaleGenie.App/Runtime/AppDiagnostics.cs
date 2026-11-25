using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;

using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Writes failures that happen outside a macro step, where no run log can hold them. The trail is
/// deliberately bounded: the current file and two backups, each under 512 KB, so a long-running
/// program cannot turn diagnostics into gigabytes. Old oversized backups from a previous cap are
/// pruned on the next write.
/// </summary>
public static class AppDiagnostics
{
    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, DateTime> LastWritten =
        new(StringComparer.OrdinalIgnoreCase);
    private const long MaximumBytes = 512 * 1024;
    private const int BackupCount = 2;

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
            Append(new StringBuilder()
                .AppendLine(CultureInfo.InvariantCulture, $"[{now:O}] {stage}")
                .AppendLine(CultureInfo.InvariantCulture, $"thread={Environment.CurrentManagedThreadId} type={error.GetType().FullName}")
                .AppendLine(error.ToString())
                .AppendLine()
                .ToString());
        }
        catch
        {
            // Diagnostics must never become another failure.
        }
    }

    /// <summary>
    /// One line about something that worked or did not, for the parts of the interface whose
    /// failure is only visible to the person using it — a menu that never appeared leaves nothing
    /// behind but "nothing happened", and that is not something the next diagnosis can read.
    /// </summary>
    public static void Note(string stage, string note)
    {
        try
        {
            Append($"[{DateTime.Now:O}] {stage} {note}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics must never become another failure.
        }
    }

    /// <summary>Writes one record out, rolling the file over when it has grown too far.</summary>
    private static void Append(string text)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(AppPaths.Logs);
            var path = FilePath;
            PruneBackups(path);
            var incoming = new UTF8Encoding(false).GetByteCount(text);
            if (File.Exists(path)
                && new FileInfo(path).Length + incoming > MaximumBytes)
            {
                // Keep a small, bounded trail. The oldest copy is dropped rather than growing a
                // numbered archive: these are crash clues for the next start, not a history.
                var oldest = path + "." + BackupCount.ToString(CultureInfo.InvariantCulture);
                if (File.Exists(oldest))
                {
                    File.Delete(oldest);
                }

                for (var index = BackupCount - 1; index >= 1; index--)
                {
                    var from = path + "." + index.ToString(CultureInfo.InvariantCulture);
                    var to = path + "." + (index + 1).ToString(CultureInfo.InvariantCulture);
                    if (File.Exists(from))
                    {
                        File.Move(from, to, true);
                    }
                }

                File.Move(path, path + ".1", true);
            }

            File.AppendAllText(path, text, new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Drops an old backup left by a larger cap or an older build. Without this the new cap would
    /// only apply to files written from now on, and the oversized pair would stay beside it.
    /// </summary>
    private static void PruneBackups(string path)
    {
        for (var index = 1; index <= BackupCount; index++)
        {
            var backup = path + "." + index.ToString(CultureInfo.InvariantCulture);
            if (File.Exists(backup) && new FileInfo(backup).Length > MaximumBytes)
            {
                File.Delete(backup);
            }
        }
    }
}
