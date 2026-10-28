using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace WhaleGenie.Mcp;

/// <summary>
/// Checks a WhaleGenie MCP server from outside and prints what it found. It runs before Avalonia
/// exists and never touches a project of its own: it starts a server on a copy of a package, speaks
/// to it down the pipe it owns, and kills it afterwards. The exit code is the answer a script reads;
/// the markdown is the answer a person reads.
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
            var started = Spawn(options);
            if (started is null)
            {
                return 4;
            }

            child = started.Process;
            folder = started.Folder;

            // The child's own stdin and stdout are the pipe — the same transport a client gets, so
            // the check exercises the arrangement the program is actually used in. Nothing is read
            // from that pipe but the protocol: an address line on stdout would now be a message.
            var transport = new StreamClientTransport(
                child.StandardInput.BaseStream, child.StandardOutput.BaseStream, null);

            using var connect = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            connect.CancelAfter(Startup);

            McpClient client;
            try
            {
                client = await McpClient.CreateAsync(transport, cancellationToken: connect.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                Console.Error.WriteLine($"whalegenie: the server did not answer: {error.Message}");
                Explain(started);
                return 4;
            }

            await using var session = client;

            // The check always starts the server it looks at, and that server always gets a copy of
            // the package, so applying and saving are safe: a doctor that rewrote the person's
            // package would be the very thing it is meant to catch.
            var report = await new McpSelfCheck(client, new McpSelfCheckOptions
            {
                Where = "stdio",
                AllowApply = true,
                AllowSave = true,
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
    /// point: the check applies and saves, and that must not land in anything of the person's.
    /// </summary>
    private static Spawned? Spawn(McpDoctorOptions options)
    {
        var executable = McpProgram.Executable;
        if (executable.Length == 0)
        {
            Console.Error.WriteLine(
                "whalegenie: cannot find WhaleGenie.exe beside this program, so there is no server to start.");
            return null;
        }

        var folder = Path.Combine(Path.GetTempPath(), "wg-mcp-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("--mcp");

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
        var drained = DrainAsync(process, log);
        return new Spawned(process, folder, log, drained);
    }

    /// <summary>What the child said on its own error stream, for a check that could not talk to it.</summary>
    private static void Explain(Spawned started)
    {
        started.Drained.Wait(TimeSpan.FromSeconds(2));
        lock (started.Log)
        {
            if (started.Log.Length > 0)
            {
                Console.Error.WriteLine(started.Log.ToString());
            }
        }
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

    private sealed record Spawned(Process Process, string Folder, StringBuilder Log, Task Drained);
}
