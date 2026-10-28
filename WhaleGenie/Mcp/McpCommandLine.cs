using System;
using System.Globalization;
using System.IO;

namespace WhaleGenie.Mcp;

/// <summary>What a run without a window was asked to serve.</summary>
internal sealed record McpHostOptions(int Port, string? PackagePath, string? NewFolder,
    bool AllowControl, bool AllowRun);

/// <summary>What a doctor run was asked to look at.</summary>
internal sealed record McpDoctorOptions(
    string? Url, bool Spawn, string? PackagePath, string? Report, bool Write, bool Strict);

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
        whalegenie --mcp [--port N] [--open FILE.wgmacro] [--new FOLDER]
                       [--allow-control] [--allow-run]
          Serves MCP without a window until Ctrl+C. Without the last two, the server can only
          read: it lists macros, reads the screen and reports state, but sends no input and runs
          no macro, because there is no window to ask about them.
        whalegenie --mcp-doctor (--url URL | --spawn) [--open FILE.wgmacro] [--report FILE] [--write] [--strict]
          Checks an MCP server and prints what it found.
        """;

    internal static bool HostAsked(string[] args) => args.Length > 0 && args[0] == HostFlag;

    internal static bool DoctorAsked(string[] args) => args.Length > 0 && args[0] == DoctorFlag;

    /// <summary>
    /// The host command, or null with a reason. A wrong command line is answered with the usage
    /// rather than by starting something that is not what was asked for.
    /// </summary>
    internal static McpHostOptions? ParseHost(string[] args, out string error)
    {
        var port = 0;
        string? open = null;
        string? folder = null;
        var allowControl = false;
        var allowRun = false;
        error = string.Empty;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--port":
                    if (!Next(args, ref index, out var value)
                        || !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out port)
                        || port is < 0 or > 65535)
                    {
                        error = "--port needs a number from 0 to 65535; 0 lets the system choose one.";
                        return null;
                    }

                    break;
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

        return new McpHostOptions(port, open is null ? null : Path.GetFullPath(open),
            folder is null ? null : Path.GetFullPath(folder), allowControl, allowRun);
    }

    /// <summary>The doctor command, or null with a reason.</summary>
    internal static McpDoctorOptions? ParseDoctor(string[] args, out string error)
    {
        string? url = null;
        string? open = null;
        string? report = null;
        var spawn = false;
        var write = false;
        var strict = false;
        error = string.Empty;

        for (var index = 1; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--url":
                    if (!Next(args, ref index, out url)
                        || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                        || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                    {
                        error = $"--url needs an http address such as http://localhost:3917/mcp; \"{url}\" is not one.";
                        return null;
                    }

                    break;
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
                case "--spawn":
                    spawn = true;
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

        if (url is null && !spawn)
        {
            error = "Use --url for a server that is already running, or --spawn to start one for the check.";
            return null;
        }

        if (url is not null && spawn)
        {
            error = "Use --url or --spawn, not both.";
            return null;
        }

        return new McpDoctorOptions(url,
            spawn,
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
