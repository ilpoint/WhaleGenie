using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Core.Devices;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
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
        // The session every run of this project shares is made here, before any window: what a
        // macro wrote last time is read back into it, and the windows and the MCP tools are handed
        // this one table rather than reaching for a static of their own.
        var variables = new VariableSession();
        var persisted = new PersistedVariables(PersistedVariables.DefaultPath);
        persisted.Initialize(variables);

        // The MCP flyout is a recipe now, not a server: the program no longer listens on anything,
        // so nothing in the container has to be built for it.
        var services = Wiring(
            _ => new MainViewModel(_ => new McpServerViewModel(), persisted, variables), persisted,
            variables);

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
    internal static ServiceProvider Minimal(ProjectWorkspace project)
    {
        var viewModel = new MainViewModel(workspace: project);
        return Wiring(_ => viewModel, project.Persisted, project.Variables).BuildServiceProvider();
    }

    /// <summary>
    /// The server over this run's container. The tool owners are named here rather than inside the
    /// server, so adding a tool is one registration beside the others instead of an edit in a
    /// second project.
    /// </summary>
    internal static McpServerHost Host(IServiceProvider services)
        => new(services, Owners(services));

    /// <summary>The same graph a check builds when it has a project and no window.</summary>
    internal static McpServerHost HostFor(ProjectWorkspace project)
    {
        var services = Minimal(project);
        return new McpServerHost(services, Owners(services), ownsServices: true);
    }

    private static IReadOnlyList<object> Owners(IServiceProvider services)
        =>
        [
            services.GetRequiredService<McpTools>(),
            services.GetRequiredService<McpControlTools>(),
            services.GetRequiredService<McpRunTools>(),
            services.GetRequiredService<McpRunLogTools>(),
        ];

    private static ServiceCollection Wiring(Func<IServiceProvider, MainViewModel> project,
        PersistedVariables persisted, VariableSession variables)
    {
        var services = new ServiceCollection();

        services.AddSingleton(sp => project(sp));
        services.AddSingleton(sp => sp.GetRequiredService<MainViewModel>().Workspace);
        services.AddSingleton(persisted);
        services.AddSingleton(variables);

        // One device layer for the whole run: a window, a trigger and an MCP call all work on this
        // machine, and the models it loads are loaded once.
        services.AddSingleton<IDeviceLayer>(_ => SharedDevices.Current);

        services.AddSingleton<McpDraftStore>();
        services.AddSingleton<McpLiveProject>();
        services.AddSingleton<IProjectGateway>(sp => sp.GetRequiredService<McpLiveProject>());
        services.AddSingleton<McpTools>();
        services.AddSingleton<McpRunLogTools>();

        // The triggers are the window's in practice, but not in principle: they are a service that
        // outlives one window and is stopped and released with the run.
        services.AddSingleton(sp => new TriggerRuntime(
            sp.GetRequiredService<IDeviceLayer>(), sp.GetRequiredService<VariableSession>()));

        // The two tool sets that touch the machine take the layer through a function rather than an
        // instance: a run that never asks for a device never builds one.
        services.AddSingleton(sp => new McpControlTools(() => sp.GetRequiredService<IDeviceLayer>()));
        services.AddSingleton(sp => new McpRunTools(
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<McpDraftStore>(),
            () => sp.GetRequiredService<IDeviceLayer>()));

        services.AddSingleton<McpResources>();
        services.AddSingleton(sp => Host(sp));

        return services;
    }
}
