using System;
using System.IO;

namespace WhaleGenie.Mcp;

/// <summary>What a run without a window was asked to serve.</summary>
internal sealed record McpHostOptions(string? PackagePath, string? NewFolder,
    bool AllowControl, bool AllowRun);

/// <summary>What a doctor run was asked to look at.</summary>
internal sealed record McpDoctorOptions(string? PackagePath, string? Report, bool Write, bool Strict);

/// <summary>
/// The two command lines beside the window. They are read before Avalonia exists, so they use
/// nothing but strings and the file system: a command that has not built an application cannot
/// borrow its services.
/// </summary>
internal static class McpCommandLine
{
    internal const string HostFlag = "--mcp";
    internal const string DoctorFlag = "--mcp-doctor";

    internal static string Usage => """
        whalegenie --mcp [--open FILE.wgmacro] [--new FOLDER] [--allow-control] [--allow-run]
          Serves MCP over stdin and stdout. The client launches this program and owns the pipe; when
          it closes, this exits. Without the last two flags the server can only read: it lists
          macros, reads the screen and reports state, but sends no input and runs no macro, because
          there is no window to ask about them.
        whalegenie --mcp-doctor [--open FILE.wgmacro] [--report FILE] [--write] [--strict]
          Starts a server of its own over a pipe and prints what it found.
        """;

    internal static bool HostAsked(string[] args) => args.Length > 0 && args[0] == HostFlag;

    internal static bool DoctorAsked(string[] args) => args.Length > 0 && args[0] == DoctorFlag;

    /// <summary>
    /// The host command, or null with a reason. A wrong command line is answered with the usage
    /// rather than by starting something that is not what was asked for.
    /// </summary>
    internal static McpHostOptions? ParseHost(string[] args, out string error)
    {
        string? open = null;
        string? folder = null;
        var allowControl = false;
        var allowRun = false;
        error = string.Empty;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--open":
                    if (!Next(args, ref index, out open) || !File.Exists(open))
                    {
                        error = $"--open needs a package that exists; \"{open}\" is not one.";
                        return null;
                    }

                    break;
                case "--new":
                    if (!Next(args, ref index, out folder) || folder.Length == 0)
                    {
                        error = "--new needs a folder to write the empty project into.";
                        return null;
                    }

                    break;
                case "--allow-control":
                    allowControl = true;
                    break;
                case "--allow-run":
                    allowRun = true;
                    break;
                default:
                    error = $"Unknown argument \"{args[index]}\".";
                    return null;
            }
        }

        if (open is not null && folder is not null)
        {
            error = "Use --open or --new, not both.";
            return null;
        }

        return new McpHostOptions(open is null ? null : Path.GetFullPath(open),
            folder is null ? null : Path.GetFullPath(folder), allowControl, allowRun);
    }

    /// <summary>The doctor command, or null with a reason.</summary>
    internal static McpDoctorOptions? ParseDoctor(string[] args, out string error)
    {
        string? open = null;
        string? report = null;
        var write = false;
        var strict = false;
        error = string.Empty;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--open":
                    if (!Next(args, ref index, out open) || !File.Exists(open))
                    {
                        error = $"--open needs a package that exists; \"{open}\" is not one.";
                        return null;
                    }

                    break;
                case "--report":
                    if (!Next(args, ref index, out report) || report.Length == 0)
                    {
                        error = "--report needs a file to write the markdown to.";
                        return null;
                    }

                    break;
                case "--write":
                    write = true;
                    break;
                case "--strict":
                    strict = true;
                    break;
                default:
                    error = $"Unknown argument \"{args[index]}\".";
                    return null;
            }
        }

        return new McpDoctorOptions(
            open is null ? null : Path.GetFullPath(open),
            report is null ? null : Path.GetFullPath(report),
            write,
            strict);
    }

    private static bool Next(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }
}
