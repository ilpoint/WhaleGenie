using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Runtime;

namespace WhaleGenie.Mcp;

/// <summary>
/// Serves MCP from a run that never shows a window and never builds Avalonia. The project document,
/// the draft store and the tools are plain objects; the only thread work there is goes through the
/// direct dispatcher the container registers for a windowless run.
/// </summary>
internal static class McpHeadless
{
    /// <summary>
    /// Runs until the client closes the pipe or the person presses Ctrl+C, and reports the code the
    /// process should end with. It runs before Avalonia is started, so a client that only edits a
    /// project never pays for a window toolkit.
    /// </summary>
    internal static int Run(McpHostOptions options)
    {
        RuntimeSetup.Prepare(AppSettings.Shared);

        var services = AppServices.Windowless(out var project);

        // A windowless run has no page to flip, but the page is in the program the person has open
        // beside it: the two permissions are read back from the settings file before each call, so
        // switching one over there takes effect here on the next call rather than at the next start.
        McpControl.Stored = () => McpGrants.Current(AppSettings.Shared);

        if (Setup(project, services.GetRequiredService<ProjectFileService>(), options) is { } failure)
        {
            Console.Error.WriteLine($"whalegenie: {failure}");
            services.Dispose();
            return 2;
        }

        var host = AppServices.Host(services);
        try
        {
            host.Start(Console.OpenStandardInput(), Console.OpenStandardOutput());
            Console.Error.WriteLine("whalegenie: MCP on stdin/stdout; closing the pipe stops it");
            return Wait(host).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"whalegenie: the MCP server did not start: {error.Message}");
            return 3;
        }
        finally
        {
            try
            {
                host.StopAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // A stop that failed still has to release the session; the process is leaving.
            }

            try
            {
                project.Persisted.Flush();
            }
            catch
            {
                // A value that will not write must not keep the process alive.
            }

            services.Dispose();
            SharedDevices.Release();
        }
    }

    /// <summary>
    /// Waits for the client to close the pipe, or for Ctrl+C to ask for a stop. Both are ordinary
    /// endings; a stop asked for here is carried out before the answer comes back.
    /// </summary>
    private static async Task<int> Wait(McpServerHost host)
    {
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Console.CancelKeyPress += (_, keys) =>
        {
            // The runtime would end the process here, which would leave the session to be reaped
            // with it. Asking for a stop instead is what makes Ctrl+C feel like closing the pipe.
            keys.Cancel = true;
            stop.TrySetResult();
        };

        var closed = host.WaitForShutdownAsync();
        var ended = await Task.WhenAny(closed, stop.Task).ConfigureAwait(false);
        if (ended == stop.Task)
        {
            await host.StopAsync().ConfigureAwait(false);
        }
        else
        {
            await closed.ConfigureAwait(false);
        }

        return 0;
    }

    /// <summary>
    /// Opens the project this run serves, or makes an empty one. Nothing here touches a window.
    /// </summary>
    private static string? Setup(ProjectWorkspace project, ProjectFileService files,
        McpHostOptions options)
    {
        try
        {
            if (options.PackagePath is { } path)
            {
                var opened = files.Open(project, path);
                return opened.Ok ? null : $"cannot open the project: {opened.Error?.Message}";
            }

            if (options.NewFolder is { } folder)
            {
                Directory.CreateDirectory(folder);
                var created = Path.Combine(folder, "doctor.wgmacro");
                project.CurrentPath = created;
                var saved = files.Save(project, created);
                return saved.Ok ? null : $"cannot write the project: {saved.Error?.Message}";
            }

            // Neither told which package to serve: the one the window had open last is what the
            // client means by "my project". A path that is gone leaves this run with nothing, which
            // is still better than failing to start.
            if (AppSettings.Shared.LastProject() is { Length: > 0 } last && File.Exists(last))
            {
                var opened = files.Open(project, last);
                if (!opened.Ok)
                {
                    return $"cannot open the project: {opened.Error?.Message}";
                }
            }

            return null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return $"cannot open the project: {error.Message}";
        }
    }
}
