using System;
using System.IO;

namespace WhaleGenie.Mcp;

/// <summary>
/// This program's own executable, which is what a client's configuration has to name: the client
/// starts it, so the path it writes down must be the one that really exists — the running process
/// when that is WhaleGenie itself, or the copy beside this one when a check is running the tests.
/// </summary>
internal static class McpProgram
{
    internal static string Executable
    {
        get
        {
            var running = Environment.ProcessPath;
            if (running is { Length: > 0 }
                && Path.GetFileNameWithoutExtension(running)
                    .Equals("WhaleGenie", StringComparison.OrdinalIgnoreCase))
            {
                return running;
            }

            var beside = Path.Combine(AppContext.BaseDirectory, "WhaleGenie.exe");
            return File.Exists(beside) ? beside : string.Empty;
        }
    }
}
