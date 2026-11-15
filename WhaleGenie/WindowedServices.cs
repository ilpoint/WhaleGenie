using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie;

/// <summary>
/// The composition root of the windowed program: the graph every run shares, plus the one thing a
/// run with a window needs and a run without one must not compile into its path — the window and the
/// view model behind it. Keeping those two in their own method is what lets the headless entry point
/// build the same graph without touching Avalonia.
/// </summary>
internal static class WindowedServices
{
    /// <summary>A run with a window on top of it, which is the ordinary way the program is started.</summary>
    internal static ServiceProvider Build()
    {
        // The session every run of this project shares is made here, before any window: what a
        // macro wrote last time is read back into it, and the windows and the MCP tools are handed
        // this one table rather than reaching for a static of their own.
        var variables = new VariableSession();
        var persisted = new PersistedVariables(PersistedVariables.DefaultPath);
        persisted.Initialize(variables);

        // The document the window shows is the one the runs read: the view model makes it, and the
        // container hands that same instance to every tool and trigger.
        var services = AppServices.Wiring(
            sp => sp.GetRequiredService<MainViewModel>().Workspace,
            persisted,
            variables,
            new AvaloniaUiDispatcher(),
            new AvaloniaWindowAside());

        services.AddSingleton(_ => new MainViewModel(_ => new McpServerViewModel(), persisted,
            variables));
        Add(services);

        return services.BuildServiceProvider();
    }

    private static void Add(ServiceCollection services)
        =>
        // The view model the window shows is the same instance the MCP server reads.
        services.AddSingleton(sp => new MainWindow(sp.GetRequiredService<TriggerRuntime>())
        {
            DataContext = sp.GetRequiredService<MainViewModel>(),
        });
}
