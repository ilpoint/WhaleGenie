using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Bridge;
using WhaleGenie.Core.Devices;
using WhaleGenie.Editing;
using WhaleGenie.Editor;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
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
            new AvaloniaWindowAside(),
            AppSettings.Shared);

        services.AddSingleton(sp => new MainViewModel(_ => new McpServerViewModel(), persisted,
            variables, workspace, sp.GetRequiredService<SystemState>()));
        Add(services);

        return services.BuildServiceProvider();
    }

    private static void Add(IServiceCollection services) =>
        services.AddSingleton<IPackageStore, FilePackageStore>()
        .AddSingleton<IPublicLibraryStore>(_ => new FilePublicLibraryStore())
        .AddSingleton<ProjectFileService>()
        .AddSingleton(sp => new PackageTransferService(
            sp.GetRequiredService<IPackageStore>(),
            sp.GetRequiredService<IPublicLibraryStore>()))
        .AddSingleton<RunQuestionHost>()
        .AddSingleton(sp => new ProjectSaver(
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<ProjectFileService>()))
        .AddSingleton<IProjectSaver>(sp => sp.GetRequiredService<ProjectSaver>())
        .AddSingleton(sp => new ShellProjectChangeGuard(() => sp.GetService<WebShellWindow>()))
        .AddSingleton<IProjectChangeGuard>(sp => sp.GetRequiredService<ShellProjectChangeGuard>())
        .AddSingleton(sp => new McpLiveProject(
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<IUiDispatcher>(),
            sp.GetRequiredService<IPackageStore>(),
            sp.GetRequiredService<ProjectSaver>(),
            sp.GetRequiredService<IEditorDocuments>()))
        .AddSingleton(sp => new SystemBridge(
            sp.GetRequiredService<SystemState>(), sp.GetRequiredService<AppSettings>()))
        .AddSingleton(sp => new EditorDocumentService(
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<TriggerRuntime>().Devices,
            new LocalEditorSettings(sp.GetRequiredService<AppSettings>()),
            sp.GetRequiredService<IPublicLibraryStore>()))
        .AddSingleton(sp => new EditorSessionBridge(
            sp.GetRequiredService<EditorDocumentService>(),
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<TriggerRuntime>().Devices,
            new LocalEditorSettings(sp.GetRequiredService<AppSettings>()),
            new EditorPickerHost(() => sp.GetService<WebShellWindow>(),
                sp.GetRequiredService<TriggerRuntime>().Devices),
            new EditorWindowAside(() => sp.GetService<WebShellWindow>()),
            new EditorBrowserTestHost(),
            new EditorElementTestHost(() => sp.GetService<WebShellWindow>()),
            sp.GetRequiredService<IPublicLibraryStore>(),
            sp.GetRequiredService<IPackageStore>(),
            sp.GetRequiredService<AppSettings>()))
        .AddSingleton<IEditorDocuments>(sp => sp.GetRequiredService<EditorDocumentService>())
        .AddSingleton<EditorLeavePrompt>()
        .AddSingleton<IEditorLeavePrompt>(sp => sp.GetRequiredService<EditorLeavePrompt>())
        .AddSingleton(sp => new ShellHostFactory(sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<SystemState>(), sp.GetRequiredService<TriggerRuntime>(),
            sp.GetRequiredService<RunQuestionHost>(),
            sp.GetRequiredService<MenuSurfaceBridge>(),
            sp.GetRequiredService<WheelSurfaceBridge>(),
            sp.GetRequiredService<PanelSurfaceBridge>(),
            sp.GetRequiredService<AppSettings>(),
            sp.GetRequiredService<IProjectSaver>(),
            sp.GetRequiredService<ProjectFileService>(),
            sp.GetRequiredService<IEditorDocuments>(),
            sp.GetRequiredService<IEditorLeavePrompt>()))
        .AddSingleton(sp => new PanelSurfaceBridge(new PanelSurface(
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<TriggerRuntime>())))
        .AddSingleton(sp => new WheelSurfaceBridge(new WheelSurface(
            sp.GetRequiredService<ProjectWorkspace>())))
        .AddSingleton(sp => new MenuSurfaceBridge(new MenuSurface(
            sp.GetRequiredService<ProjectWorkspace>(),
            sp.GetRequiredService<IDeviceLayer>().Windows,
            sp.GetRequiredService<TriggerRuntime>()),
            new ProjectDialogs(() => sp.GetService<WebShellWindow>())))
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
            server.Register(new IconBridge());
            server.Register(new ThemeBridge(new ThemeCommands(sp.GetRequiredService<AppSettings>())));
            server.Register(new WindowBridge(() =>
            sp.GetService<WebShellWindow>() is { } window ? new ShellWindowCommands(window) : null));
            server.Register(new McpBridge(() => SharedDevices.Current.Clipboard,
                sp.GetRequiredService<AppSettings>()));
            server.Register(sp.GetRequiredService<MenuSurfaceBridge>());
            server.Register(sp.GetRequiredService<WheelSurfaceBridge>());
            server.Register(sp.GetRequiredService<PanelSurfaceBridge>());
            var dialogs = new DialogBridge();
            server.Register(dialogs);
            var project = sp.GetRequiredService<ProjectWorkspace>();
            var autoSave = sp.GetRequiredService<ProjectSaver>();
            var systemBridge = sp.GetRequiredService<SystemBridge>();
            server.Register(systemBridge);
            var editorPrompt = sp.GetRequiredService<EditorLeavePrompt>();
            server.Register(editorPrompt);
            server.Register(new ProjectBridge(project,
                sp.GetRequiredService<TriggerRuntime>(), sp.GetRequiredService<SystemState>(),
                new ProjectDialogs(() => sp.GetService<WebShellWindow>()),
                sp.GetRequiredService<ProjectFileService>(),
                sp.GetRequiredService<PackageTransferService>(), dialogs,
                settings: sp.GetRequiredService<AppSettings>(),
                saver: sp.GetRequiredService<IProjectSaver>(),
                changeGuard: sp.GetRequiredService<IProjectChangeGuard>(),
                ui: new AvaloniaUiPost()));
            var devices = sp.GetRequiredService<TriggerRuntime>().Devices;
            server.Register(sp.GetRequiredService<EditorSessionBridge>());
            server.Register(new LibraryBridge(sp.GetRequiredService<ProjectWorkspace>(), dialogs,
                store: sp.GetRequiredService<IPublicLibraryStore>(),
                packages: sp.GetRequiredService<IPackageStore>()));
            var main = sp.GetRequiredService<MainViewModel>();
            var settings = new SettingsViewModel(
                SharedDevices.Current.Driver, null, main.Workspace,
                main.Workspace.CurrentPath, main.MarkMenuChanged,
                main.SystemSwitch, new MenuIconCatalogAdapter(),
                sp.GetRequiredService<AppSettings>());
            server.Register(new WindowListBridge(devices.Windows,
                new WindowPickHost(() => sp.GetService<WebShellWindow>(), devices)));
            server.Register(new SettingsBridge(settings, main.Workspace,
                () =>
                {
                    if (sp.GetService<WebShellWindow>()?.Actions is { } shell)
                    {
                        shell.RefreshAfterSettings();
                    }

                    systemBridge.Publish();
                    autoSave.OnSettingsChanged();
                    sp.GetService<EditorSessionBridge>()?.PublishPublicMacros();
                }));
            server.Register(new RunBridge(sp.GetRequiredService<ProjectWorkspace>(),
                sp.GetRequiredService<TriggerRuntime>(), new AvaloniaUiPost()));
            return server;
        })
        .AddSingleton(sp => new WebShellWindow(sp.GetRequiredService<BridgeServer>(),
            sp.GetRequiredService<ShellHostFactory>()));
}
