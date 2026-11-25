using System;
using System.IO;
using System.Linq;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// Diagnostics are a short trail for the next diagnosis, not an unbounded archive. The writer keeps
/// the current file and two older ones, each under half a megabyte, and drops the oldest rather
/// than letting a log grow with the program's uptime.
/// </summary>
public class AppDiagnosticsTests
{
    [Fact]
    public void The_runtime_log_keeps_a_small_bounded_trail()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-diagnostics-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "runtime.log");
        var previous = AppDiagnostics.FilePathOverride;
        AppDiagnostics.FilePathOverride = path;
        try
        {
            var line = new string('x', 1024);
            for (var index = 0; index < 2200; index++)
            {
                AppDiagnostics.Note("test.line", line);
            }

            var files = Directory.GetFiles(folder, "runtime.log*")
                .Select(file => new FileInfo(file))
                .ToList();
            Assert.InRange(files.Count, 1, 3);
            Assert.All(files, file => Assert.True(file.Length <= 512 * 1024,
                $"{file.Name} grew to {file.Length} bytes"));
            Assert.True(files.Sum(file => file.Length) <= 3L * 512 * 1024);
            Assert.Contains(files, file => file.Name == "runtime.log.1");
        }
        finally
        {
            AppDiagnostics.FilePathOverride = previous;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
