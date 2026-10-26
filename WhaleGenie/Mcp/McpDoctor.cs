using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;

namespace WhaleGenie.Mcp;

/// <summary>
/// Checks a WhaleGenie MCP server from outside and prints what it found. It runs before Avalonia
/// exists and never touches a project of its own: either it connects to a server somebody started,
/// or it starts one on a copy of a package and kills it afterwards. The exit code is the answer a
/// script reads; the markdown is the answer a person reads.
///
/// This is a developer's command, like the script host, so its words are English and it does not
/// go through the interface's string table.
/// </summary>
internal static class McpDoctor
{
    private static readonly TimeSpan Startup = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Whole = TimeSpan.FromMinutes(2);

    /// <summary>0 nothing failed, 1 something failed, 2 bad command line, 3 no server, 4 no child.</summary>
    internal static int Run(string[] args)
    {
        var options = McpCommandLine.ParseDoctor(args, out var error);
        if (options is null)
        {
            Console.Error.WriteLine($"whalegenie: {error}");
            Console.Error.WriteLine(McpCommandLine.Usage);
            return 2;
        }

        try
        {
            // The findings can quote a validation message, and those are Chinese on a Chinese
            // machine. A console that cannot be set must not stop the check.
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // A windowless process has no console of its own.
        }

        try
        {
            return RunAsync(options).GetAwaiter().GetResult();
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"whalegenie: {failure.Message}");
            return 3;
        }
    }

    private static async Task<int> RunAsync(McpDoctorOptions options)
    {
        using var deadline = new CancellationTokenSource(Whole);
        Process? child = null;
        string? folder = null;
        try
        {
            string endpoint;
            if (options.Spawn)
            {
                var started = Spawn(options);
                if (started is null)
                {
                    return 4;
                }

                child = started.Process;
                folder = started.Folder;
                if (await WaitForEndpointAsync(started.Process, started.Log, deadline.Token)
                        .ConfigureAwait(false) is not { } reported)
                {
                    return 4;
                }

                endpoint = reported;
            }
            else
            {
                endpoint = options.Url!;
            }

            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(endpoint),
                TransportMode = HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(15),
            }, Direct(), ownsHttpClient: true);
            await using var client = await McpClient.CreateAsync(transport,
                cancellationToken: deadline.Token).ConfigureAwait(false);

            // Writing is only ever done to a copy this command made, so a check that started its
            // own server gets the whole loop; one pointed at a server somebody is using does not
            // write unless it was asked to.
            var write = options.Write || options.Spawn;

            // A range run is checked as a whole loop rather than as a surface: what matters there is
            // that a macro really moved the window and that the edges really held.
            var report = options.Range
                ? await McpRangeCheck.RunAsync(client, endpoint, deadline.Token).ConfigureAwait(false)
                : await new McpSelfCheck(client, new McpSelfCheckOptions
                {
                    Endpoint = endpoint,
                    AllowApply = write,
                    AllowSave = write,
                }).RunAsync(deadline.Token).ConfigureAwait(false);

            if (report is null)
            {
                return 1;
            }

            var text = report.ToMarkdown();
            Console.Out.Write(text);
            Console.Out.Flush();
            if (options.Report is { } path)
            {
                var parent = Path.GetDirectoryName(path);
                if (parent is { Length: > 0 })
                {
                    Directory.CreateDirectory(parent);
                }

                File.WriteAllText(path, text, new UTF8Encoding(false));
            }

            return report.Failed > 0 || (options.Strict && report.Missing > 0) ? 1 : 0;
        }
        finally
        {
            Kill(child);
            if (folder is not null)
            {
                TryDelete(folder);
            }
        }
    }

    /// <summary>
    /// Starts this program without a window over a throwaway copy of a package. The copy is the
    /// point: the check applies and saves, and a doctor that rewrote the person's package would be
    /// the very thing it is meant to catch.
    /// </summary>
    private static Spawned? Spawn(McpDoctorOptions options)
    {
        var executable = Self();
        if (executable.Length == 0)
        {
            Console.Error.WriteLine(
                "whalegenie: cannot find WhaleGenie.exe beside this program; use --url instead.");
            return null;
        }

        var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--mcp");
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add("0");
        if (options.Range)
        {
            start.ArgumentList.Add("--range");
        }

        if (options.PackagePath is { } package)
        {
            var copy = Path.Combine(folder, Path.GetFileName(package));
            File.Copy(package, copy, overwrite: true);
            start.ArgumentList.Add("--open");
            start.ArgumentList.Add(copy);
        }
        else
        {
            // No package to copy: the child makes an empty one, which is the only way a save has
            // somewhere to go without borrowing the person's project.
            start.ArgumentList.Add("--new");
            start.ArgumentList.Add(folder);
        }

        Process? process;
        try
        {
            process = Process.Start(start);
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException)
        {
            Console.Error.WriteLine($"whalegenie: the server could not be started: {error.Message}");
            return null;
        }

        if (process is null)
        {
            Console.Error.WriteLine("whalegenie: the server could not be started.");
            return null;
        }

        var log = new StringBuilder();
        _ = DrainAsync(process, log);
        return new Spawned(process, folder, log);
    }

    /// <summary>
    /// Waits for the one line the child writes when it is ready. Anything else it says is kept, so
    /// a child that died can explain itself instead of only going quiet.
    /// </summary>
    private static async Task<string?> WaitForEndpointAsync(Process process, StringBuilder log,
        CancellationToken token)
    {
        var limit = DateTime.UtcNow + Startup;
        while (true)
        {
            var left = limit - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                break;
            }

            string? line;
            try
            {
                line = await process.StandardOutput.ReadLineAsync().WaitAsync(left, token)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                break;
            }
            catch (Exception error) when (error is IOException or InvalidOperationException
                or ObjectDisposedException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            if (line.StartsWith("endpoint=", StringComparison.Ordinal))
            {
                return line["endpoint=".Length..].Trim();
            }
        }

        Console.Error.WriteLine("whalegenie: the server did not report an address in time.");
        lock (log)
        {
            if (log.Length > 0)
            {
                Console.Error.WriteLine(log.ToString());
            }
        }

        return null;
    }

    /// <summary>Keeps the child's error stream moving: a full pipe would stop it mid-sentence.</summary>
    private static async Task DrainAsync(Process process, StringBuilder log)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                lock (log)
                {
                    log.AppendLine(line);
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException
            or ObjectDisposedException)
        {
            // The child is gone; whatever it said is already in the log.
        }
    }

    /// <summary>
    /// The client this command talks through. It never uses a proxy: the server is a helper on this
    /// machine, and a machine that sets HTTP_PROXY for the internet would otherwise have its own
    /// loopback request handed to that proxy — which answers "502" instead of "connection refused",
    /// sending whoever reads it after the wrong problem entirely.
    /// </summary>
    private static HttpClient Direct() => new(new SocketsHttpHandler
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
    })
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    /// <summary>This program's own executable, the one a child is started from.</summary>
    private static string Self()
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

    private static void Kill(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException
            or Win32Exception)
        {
            // A child that already went away is the outcome this wanted anyway.
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A killed child may hold its package for a moment; a temp folder left behind is not
            // worth failing a check over.
        }
    }

    private sealed record Spawned(Process Process, string Folder, StringBuilder Log);
}
