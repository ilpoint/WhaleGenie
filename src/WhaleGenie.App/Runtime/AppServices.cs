using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie;

/// <summary>
/// What every kind of run wires up, whether or not a window is ever shown: the project, the shared
/// variables, the draft store, what the MCP tools are built from, and the triggers. It is one place
/// because a run that had to carry one more thing had to be changed in three, and the one that got
/// missed failed in a way that read like the macro's own fault.
/// </summary>
/// <remarks>
/// What differs between a run with a window and one without is handed in rather than switched on: the
/// window marshes to its own thread and moves its windows aside for a capture, while a run with no
/// window answers on the calling thread and has nothing to move. Rows, choices and the other view
/// models built for one row of a dialog are not registered — they are values, and a container would
/// only add a lookup between the code and the object it is making.
/// </remarks>
internal static class AppServices
{
    /// <summary>
    /// A run with no window at all: the project is made here, and the caller is handed it back
    /// because it is the thing a headless start reads and flushes.
    /// </summary>
    internal static ServiceProvider Windowless(out ProjectWorkspace workspace)
    {
        var variables = new VariableSession();
        var persisted = new PersistedVariables(PersistedVariables.DefaultPath);
        persisted.Initialize(variables);

        var document = new ProjectWorkspace(persisted, variables, AppVersion.Text);
        workspace = document;
        return Wiring(_ => document, persisted, variables, DirectUiDispatcher.Instance,
            NoWindowAside.Instance, AppSettings.Shared).BuildServiceProvider();
    }

    /// <summary>
    /// The registrations one run needs, without a window. A check that makes its own project uses
    /// this to get the same graph the program has, so a tool cannot depend on something the tests
    /// never wire up.
    /// </summary>
    internal static ServiceProvider Minimal(ProjectWorkspace project)
        => Wiring(_ => project, project.Persisted, project.Variables, DirectUiDispatcher.Instance,
            NoWindowAside.Instance, AppSettings.Shared).BuildServiceProvider();

    /// <summary>
    /// The server over this run's container. The two assemblies the tools live in are named here
    /// because the composition root is what knows both halves; the tools themselves are not, so a
    /// new one is a method with a declaration rather than an entry in a second list.
    /// </summary>
    internal static McpServerHost Host(IServiceProvider services)
        => new(services, ToolAssemblies);

    /// <summary>The same graph a check builds when it has a project and no window.</summary>
    internal static McpServerHost HostFor(ProjectWorkspace project)
    {
        var services = Minimal(project);
        return new McpServerHost(services, ToolAssemblies, ownsServices: true);
    }

    /// <summary>
    /// Where the server looks for tools: this server's own project editing tools, and the machine,
    /// run, run log and project tools the application adds. Both are scanned, so adding a tool on
    /// either side needs no change here.
    /// </summary>
    private static readonly IReadOnlyList<Assembly> ToolAssemblies =
        [typeof(McpTools).Assembly, typeof(McpRunTools).Assembly];

    /// <summary>
    /// Everything a run needs, with the two pieces that differ between a window and no window left
    /// to the caller. The project is asked of the container so the windowed root can hand in the one
    /// its window shows.
    /// </summary>
    internal static ServiceCollection Wiring(Func<IServiceProvider, ProjectWorkspace> project,
        PersistedVariables persisted, VariableSession variables, IUiDispatcher ui, IWindowAside aside,
        AppSettings settings)
    {
        var services = new ServiceCollection();

        services.AddSingleton(sp => project(sp));
        services.AddSingleton(persisted);
        services.AddSingleton(variables);

        // The system switch is machine state, not window state: the page, a hotkey and the native
        // window all flip the same object, and the trigger host is the one that follows it.
        services.AddSingleton(_ => new SystemState(settings));

        // One device layer for the whole run: a window, a trigger and an MCP call all work on this
        // machine, and the models it loads are loaded once.
        services.AddSingleton<IDeviceLayer>(_ => SharedDevices.Current);

        services.AddSingleton(ui);
        services.AddSingleton(aside);

        // One settings object for the whole run: a window, the page and a check all read and write
        // the same file through this instance rather than through a static of its own.
        services.AddSingleton(settings);

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
        services.AddSingleton(sp => new McpControlTools(
            () => sp.GetRequiredService<IDeviceLayer>(),
            sp.GetRequiredService<IWindowAside>()));
        services.AddSingleton(sp => new McpRunTools(
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<McpDraftStore>(),
            sp.GetRequiredService<IUiDispatcher>(),
            () => sp.GetRequiredService<IDeviceLayer>()));

        services.AddSingleton<McpResources>();
        services.AddSingleton(sp => Host(sp));

        return services;
    }
}
