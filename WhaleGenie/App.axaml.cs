using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie;

public partial class App : Application
{
    /// <summary>
    /// What a launch that serves MCP without a window asked for, or null for the ordinary one. It is
    /// set before the application is built, because the command line is read before there is one.
    /// </summary>
    internal static McpHostOptions? Headless { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Both kinds of run open a project and may look at the screen, so both get these two.
        PrepareProject();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (Headless is { } options)
            {
                // Nothing will ever be shown, so the lifetime must not end when the last window
                // closes: there was never a window to close, and the server is the whole run.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                McpHeadless.Start(desktop, options);
            }
            else
            {
                PrepareWindow();
                ShowWindow(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>What a project needs whatever kind of run this is.</summary>
    private static void PrepareProject()
    {
        // Values a macro wrote are read back by the project before it is handed to an MCP client.
        PersistentVariables.Initialize();

        // How this machine reads the screen is what a capture answers with, so it is told here
        // rather than when a window happens to open.
        CaptureSetup.Load();
    }

    /// <summary>
    /// What only a run with a window needs: something to refresh, something to pack, and the
    /// machine's own devices ready for a person to press Run.
    /// </summary>
    private static void PrepareWindow()
    {
        // Text computed in code is refreshed by walking the open windows; the indexer bindings
        // refresh themselves when the table raises its change.
        Strings.Current.LanguageChanged += LanguageRefresh.Refresh;

        ThemeController.Apply(ThemeController.Load());

        // Whether the virtual keyboard and mouse are to be put on the machine at startup was chosen
        // in the settings window and written beside the program; the engine does not read that file,
        // so it is told here, before anything can ask for driver-level input.
        SharedDevices.Current.Driver.AttachAtStart = LocalSettings.LoadDriverAttachAtStart();

        // And then it is given a head start: putting the virtual keyboard and mouse on the machine
        // is what the first driver-level move of a macro would otherwise wait for. Nothing is done
        // unless the settings window asked for it.
        _ = SharedDevices.Current.Driver.WarmUp();

        // Reading text loads a model, which costs a moment the first step that reads would
        // otherwise pay. Only when the setting says so: the models stay in memory for the rest of
        // the run, and a machine that never reads text should not pay for one that does.
        if (LocalSettings.LoadPreloadOcr())
        {
            _ = Task.Run(SharedDevices.WarmUpOcr);
        }
    }

    private static void ShowWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var services = AppServices.Build();
        var window = services.GetRequiredService<MainWindow>();
        var viewModel = (MainViewModel)window.DataContext!;

        viewModel.Mcp.EnsureSaved = window.EnsureProjectSavedForMcpAsync;

        desktop.MainWindow = window;

        // From here on, the window is put out of the way rather than closed, and the icon in the
        // notification area is how it comes back and how the program is stopped. The window is
        // given the icon as well, because the icon's menu is the one close that really leaves —
        // and that is the one time the window has to ask about work that is not saved.
        window.Tray = AppTray.Attach(window, desktop, () => new WindowsTray(
            Strings.Get("Tray.Tooltip"), Strings.Get("Tray.Show"), Strings.Get("Tray.Exit")));

        // The list starts empty, so if the last run was stopped without saving, the work it was
        // holding is offered back here — once the window is up, because it is a dialog. The window
        // asks about it only once in the whole run: it is announced again every time it comes back
        // from the notification area, and that is not the last run.
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => _ = window.OfferRecoveryAsync());

        desktop.Exit += (_, _) =>
        {
            PersistentVariables.Flush();
            window.Tray?.Dispose();

            // The MCP server owns a Kestrel socket; it has to be gone before the process is. This
            // event runs on the UI thread, and an MCP request can be waiting for that same thread
            // while Kestrel drains it. Blocking here on the UI thread deadlocks the exit, which is
            // exactly what left a windowless WhaleGenie process behind. Stop on a worker, wait only
            // briefly, and let Program.Run's final exit cover a host that still has not let go.
            try
            {
                Task.Run(() => viewModel.Mcp.StopAsync()).Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // The host has stopped or is unreachable; the process is leaving anyway.
            }

            // The program owns the device layer every window ran on, so this is where the models it
            // loaded and the capture sessions it opened are let go.
            try
            {
                SharedDevices.Release();
            }
            catch
            {
                // A device that will not let go must not stop the exit before Program takes the
                // virtual keyboard and mouse off the machine. Left behind, those devices are what
                // makes the pointer stutter after the window has gone.
            }

            // The container goes last: it is what built the server and the tools, and disposing it
            // before the device layer would take the two apart in the wrong order.
            services.Dispose();
        };
    }
}
