using System;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Core.Devices;
using WhaleGenie.Mcp;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie;

/// <summary>
/// The composition root. This is the one place that decides which implementation of each thing a
/// run uses and in what order they are built; everything else takes what it needs through its own
/// constructor.
/// </summary>
/// <remarks>
/// What is registered here is what has state and is shared: the device layer, the project, the draft
/// store, the MCP server and its tools. Rows, choices and the other view models built for one row of
/// a dialog are not registered — they are values, and a container would only add a lookup between the
/// code and the object it is making.
/// </remarks>
internal static class AppServices
{
    /// <summary>A run with a window on top of it, which is the ordinary way the program is started.</summary>
    internal static ServiceProvider Build(bool windowed = true)
    {
        var services = Wiring(sp => new MainViewModel(project =>
            new McpServerViewModel(project, () => sp.GetRequiredService<McpServerHost>())));

        if (windowed)
        {
            // The window is built here rather than in App so that "who gets which project" is
            // answered in one place: the view model it shows is the same instance the MCP server
            // reads.
            services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<TriggerRuntime>())
            {
                DataContext = sp.GetRequiredService<MainViewModel>(),
            });
        }

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The registrations one run needs, without a window. A check that makes its own project uses
    /// this to get the same graph the program has, so a tool cannot depend on something the tests
    /// never wire up.
    /// </summary>
    internal static ServiceProvider Minimal(MainViewModel project)
        => Wiring(_ => project).BuildServiceProvider();

    private static ServiceCollection Wiring(Func<IServiceProvider, MainViewModel> project)
    {
        var services = new ServiceCollection();

        services.AddSingleton(sp => project(sp));

        // One device layer for the whole run: a window, a trigger and an MCP call all work on this
        // machine, and the models it loads are loaded once.
        services.AddSingleton<IDeviceLayer>(_ => SharedDevices.Current);

        services.AddSingleton<McpDraftStore>();
        services.AddSingleton<McpLiveProject>();
        services.AddSingleton<McpTools>();

        // The triggers are the window's in practice, but not in principle: they are a service that
        // outlives one window and is stopped and released with the run.
        services.AddSingleton<TriggerRuntime>();

        // The two tool sets that touch the machine take the layer through a function rather than an
        // instance: a run that never asks for a device never builds one.
        services.AddSingleton(sp => new McpControlTools(() => sp.GetRequiredService<IDeviceLayer>()));
        services.AddSingleton(sp => new McpRunTools(
            sp.GetRequiredService<MainViewModel>(),
            sp.GetRequiredService<McpDraftStore>(),
            () => sp.GetRequiredService<IDeviceLayer>()));

        services.AddSingleton<McpResources>();
        services.AddSingleton(sp => new McpServerHost(
            sp.GetRequiredService<MainViewModel>(), sp));

        return services;
    }
}
