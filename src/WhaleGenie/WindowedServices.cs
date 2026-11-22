using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Bridge;
using WhaleGenie.Editing;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using WhaleGenie.Views.Pickers;
using WhaleGenie.Web;

namespace WhaleGenie;

/// <summary>
/// The composition root of the windowed program: the graph every run shares, plus the one thing a
/// run with a window needs and a run without one must not compile into its path — the window and
/// the view model behind it. Keeping those two in their own method is what lets the headless entry
/// point build the same graph without touching Avalonia.
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

        // The document is made here too, before any window: the view model, the web shell and the
        // MCP tools are handed this one instance rather than each making a table of its own.
        var workspace = new ProjectWorkspace(persisted, variables, AppVersion.Text);

        // The document the window shows is the one the runs read: the container hands that same
        // instance to every tool and trigger.
        var services = AppServices.Wiring(
            _ => workspace,
            persisted,
            variables,
            new AvaloniaUiDispatcher(),
            new AvaloniaWindowAside());

        services.AddSingleton(sp => new MainViewModel(_ => new McpServerViewModel(), persisted,
            variables, workspace, sp.GetRequiredService<SystemState>()));
        Add(services);

        return services.BuildServiceProvider();
    }

    private static void Add(IServiceCollection services)
        =>
        // The view model the shell's native windows read is the same instance the MCP server reads.
        services.AddSingleton<RunQuestionHost>()
        .AddSingleton(sp => new ShellHostFactory(sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<SystemState>(), sp.GetRequiredService<TriggerRuntime>(),
            sp.GetRequiredService<RunQuestionHost>(), sp.GetRequiredService<MainViewModel>()))
        .AddSingleton(sp =>
        {
            // The web shell's handlers are registered once and serve the one page; the window
            // bridge finds the shell through the container, which answers null while the shell
            // is not the window on duty.
            var server = new BridgeServer();
            server.Register(new AppBridge(() => AppTray.Current));
            server.Register(new ShellBridge(sp.GetRequiredService<ProjectWorkspace>(),
            new ShellCommands()));
            server.Register(new StringsBridge());
            server.Register(new ThemeBridge(new ThemeCommands()));
            server.Register(new WindowBridge(() =>
            sp.GetService<WebShellWindow>() is { } window ? new ShellWindowCommands(window) : null));
            server.Register(new McpBridge(() => SharedDevices.Current.Clipboard));
            var dialogs = new DialogBridge();
            server.Register(dialogs);
            var project = sp.GetRequiredService<ProjectWorkspace>();
            server.Register(new ProjectBridge(project,
                sp.GetRequiredService<TriggerRuntime>(), sp.GetRequiredService<SystemState>(),
                new ProjectDialogs(() => sp.GetService<WebShellWindow>()),
                new ProjectInteractionAdapter(project,
                    () => sp.GetService<WebShellWindow>(), dialogs), dialogs));
            var devices = sp.GetRequiredService<TriggerRuntime>().Devices;
            server.Register(new EditorSessionBridge(
                sp.GetRequiredService<ProjectWorkspace>(), devices,
                new LocalEditorSettings(),
                new EditorPickerHost(() => sp.GetService<WebShellWindow>(), devices),
                new EditorWindowAside(() => sp.GetService<WebShellWindow>()),
                new EditorBrowserTestHost(),
                new EditorElementTestHost(() => sp.GetService<WebShellWindow>())));
            server.Register(new LibraryBridge(sp.GetRequiredService<ProjectWorkspace>()));
            var main = sp.GetRequiredService<MainViewModel>();
            var settings = new SettingsViewModel(
                SharedDevices.Current.Driver, null, main.Workspace,
                main.Workspace.CurrentPath, main.MarkMenuChanged,
                main.SystemSwitch, new MenuIconCatalogAdapter());
            server.Register(new SettingsBridge(settings, main.Workspace, devices,
                new SettingsPickerHost(() => sp.GetService<WebShellWindow>(), devices),
                () =>
                {
                    if (sp.GetService<WebShellWindow>()?.Actions is { } shell)
                    {
                        shell.RefreshAfterSettings();
                    }
                }));
            server.Register(new RunBridge(sp.GetRequiredService<ProjectWorkspace>(),
                sp.GetRequiredService<TriggerRuntime>()));
            return server;
        })
        .AddSingleton(sp => new WebShellWindow(sp.GetRequiredService<BridgeServer>(),
            sp.GetRequiredService<ShellHostFactory>()));
}
