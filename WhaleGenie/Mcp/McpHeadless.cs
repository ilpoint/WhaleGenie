using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Mcp;

/// <summary>
/// Serves MCP from a run that never shows a window. The interface still has to exist — the project
/// is a view model and every tool reaches it on the interface thread — so this runs inside Avalonia
/// with no window rather than beside it. Without a window there is no tray, no recovery question,
/// no virtual keyboard and no OCR warm-up: this run is here to answer a client, not to work a macro.
/// </summary>
internal static class McpHeadless
{
    internal static void Start(IClassicDesktopStyleApplicationLifetime desktop, McpHostOptions options)
    {
        // A windowless run builds the same graph the windowed one does, minus the window: the tools,
        // the draft store and the project are the objects a window would have used.
        var services = AppServices.Build(windowed: false);
        var project = services.GetRequiredService<MainViewModel>();

        if (Setup(desktop, project, options) is { } failure)
        {
            Console.Error.WriteLine($"whalegenie: {failure}");
            services.Dispose();
            Later(desktop, 2);
            return;
        }

        Console.CancelKeyPress += (_, keys) =>
        {
            // The runtime would end the process here, which would leave the listener to be reaped
            // with it. Asking for a stop instead is what makes Ctrl+C feel like closing the switch.
            keys.Cancel = true;
            Stop(desktop, project, services);
        };

        _ = ServeAsync(desktop, project, options.Port);
    }

    /// <summary>
    /// Opens the project this run serves, or makes an empty one. It runs on the interface thread,
    /// which is the only place the project may be touched.
    /// </summary>
    private static string? Setup(IClassicDesktopStyleApplicationLifetime desktop,
        MainViewModel project, McpHostOptions options)
    {
        _ = desktop;
        try
        {
            if (options.PackagePath is { } path)
            {
                project.OpenPackage(path);
                return null;
            }

            if (options.NewFolder is { } folder)
            {
                Directory.CreateDirectory(folder);
                var created = Path.Combine(folder, "doctor.wgmacro");
                project.CurrentPath = created;
                project.SavePackage(created);
            }

            return null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException
            or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return $"cannot open the project: {error.Message}";
        }
    }

    private static async Task ServeAsync(IClassicDesktopStyleApplicationLifetime desktop,
        MainViewModel project, int port)
    {
        try
        {
            await project.Mcp.StartHeadlessAsync(port);

            // One line for a program, one for a person: a doctor reads stdout and nothing else, so
            // anything else written there would have to be understood by it.
            Console.Out.WriteLine($"endpoint={project.Mcp.Endpoint}");
            Console.Out.Flush();
            Console.Error.WriteLine($"whalegenie: MCP on {project.Mcp.Endpoint}; Ctrl+C stops it");
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"whalegenie: the MCP server did not start: {error.Message}");
            Later(desktop, 3);
        }
    }

    /// <summary>
    /// Asks the run to end once the loop is turning. Asking during initialization stops the
    /// dispatcher before its main loop has started, and the loop then throws on the way in — the
    /// program reports a crash instead of the reason it could not serve.
    /// </summary>
    private static void Later(IClassicDesktopStyleApplicationLifetime desktop, int code)
        => Dispatcher.UIThread.Post(() => desktop.TryShutdown(code));

    private static void Stop(IClassicDesktopStyleApplicationLifetime desktop, MainViewModel project,
        IServiceProvider services)
    {
        // Never wait for the interface thread from here: an MCP request can be waiting for that
        // same thread while this handler runs, and the two would hold each other forever.
        _ = Task.Run(async () =>
        {
            try
            {
                await project.Mcp.StopAsync();
            }
            catch
            {
                // The status already says why; the process is leaving either way.
            }

            try
            {
                PersistentVariables.Flush();
            }
            catch
            {
                // A value that will not write must not keep the process alive.
            }

            await Dispatcher.UIThread.InvokeAsync(() => desktop.TryShutdown(0));

            // The container goes last: it holds the server that was just stopped, and disposing it
            // any earlier would take the two apart in the wrong order.
            if (services is IDisposable owned)
            {
                owned.Dispose();
            }
        });
    }
}
