using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Platform.Windows;
using WhaleGenie.Projects;
using WhaleGenie.Runtime;
using WhaleGenie.Views;
using WhaleGenie.Web;

namespace WhaleGenie;

public partial class App : Application
{
#if DEBUG
    /// <summary>
    /// Whether this launch is the throwaway theme probe rather than the program. Debug only: the
    /// probe window exists to be looked at while layer one is being reviewed and then goes away.
    /// </summary>
    internal static bool ThemeProbe { get; set; }

    /// <summary>The Vite dev server the web shell loads instead of the packed page, if asked.</summary>
    internal static string? WebUiDev { get; set; }

    /// <summary>
    /// Whether this launch is the transparent-overlay probe the Lit migration's S0 gate runs. Debug
    /// only: it exists to be looked at on a real machine and then goes away with the gate.
    /// </summary>
    internal static bool OverlayProbe { get; set; }

    /// <summary>Whether the shell also opens the Lit menu overlay, for checking the host end to end.</summary>
    internal static bool MenuOverlay { get; set; }

    /// <summary>Whether to open the Lit wheel overlay at startup, for checking the host end to end.</summary>
    internal static bool WheelOverlay { get; set; }

#endif

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
#if DEBUG
        // The probe answers one question — can a transparent page sit over another program — and
        // must not touch the machine to answer it: no devices, no project, no recovery snapshot.
        if (OverlayProbe && ApplicationLifetime is IClassicDesktopStyleApplicationLifetime probeLifetime)
        {
            OverlayProbeWindow.Show(probeLifetime);
            base.OnFrameworkInitializationCompleted();
            return;
        }
#endif

        // A window is about to be shown, so the machine is prepared the same way it always was.
        // A headless MCP run prepares itself before it builds Avalonia at all.
        PrepareProject();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
#if DEBUG
            if (ThemeProbe)
            {
                // No devices, no project, no snapshot: looking at colours must not touch the machine.
                ThemeProbeWindow.Show(desktop);
            }
            else
#endif
            {
                PrepareWindow();
                ShowWindow(desktop);
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>What a project needs whatever kind of run this is.</summary>
    private static void PrepareProject() => RuntimeSetup.Prepare(AppSettings.Shared);

    /// <summary>
    /// What only a run with a window needs: something to refresh, something to pack, and the
    /// machine's own devices ready for a person to press Run.
    /// </summary>
    private static void PrepareWindow()
    {
        // Text computed in code is refreshed by walking the open windows; the indexer bindings
        // refresh themselves when the table raises its change.
        Strings.Current.LanguageChanged += LanguageRefresh.Refresh;

        ThemeController.Apply(ThemeController.Load(AppSettings.Shared));

        // Whether the virtual keyboard and mouse are to be put on the machine at startup was chosen
        // in the settings window and written beside the program; the engine does not read that file,
        // so it is told here, before anything can ask for driver-level input.
        SharedDevices.Current.Driver.AttachAtStart = AppSettings.Shared.LoadDriverAttachAtStart();

        // And then it is given a head start: putting the virtual keyboard and mouse on the machine
        // is what the first driver-level move of a macro would otherwise wait for. Nothing is done
        // unless the settings window asked for it.
        _ = SharedDevices.Current.Driver.WarmUp();

        // Reading text loads a model, which costs a moment the first step that reads would
        // otherwise pay. Only when the setting says so: the models stay in memory for the rest of
        // the run, and a machine that never reads text should not pay for one that does.
        if (AppSettings.Shared.LoadPreloadOcr())
        {
            _ = Task.Run(SharedDevices.WarmUpOcr);
        }

        // The shell draws only the words the bridge hands it, in one bundle, and building that
        // bundle is the first thing to touch the action tables. Measured on this machine, that
        // first build costs 175 ms and the ones after it about 1 ms, so it is started here beside
        // the other preloads rather than left for the page's first request to wait for.
        _ = Task.Run(() => Strings.Snapshot());
    }

    private static void ShowWindow(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // The shell is the only window. WebView2 is the one thing it cannot carry itself, so a
        // machine without the runtime gets a native explanation and no blank frame.
        if (!WebViewRuntime.IsInstalled)
        {
            AppDiagnostics.Note("webshell.runtime", "WebView2 is missing");
            WebViewMissingWindow.Show(desktop);
            return;
        }

        var services = WindowedServices.Build();
        var window = services.GetRequiredService<WebShellWindow>();
#if DEBUG
        window.DevUrl = App.WebUiDev;
#endif
        desktop.MainWindow = window;

        // The person's last package comes back with the window: an empty list every morning is a
        // step nobody asked for. A first start with no package at all lands in the default
        // project, so the first macro has somewhere to go without a Save As first. A file that is
        // not there any more leaves the window empty rather than failing to start.
        ProjectStartup.OpenAtStart(services.GetRequiredService<ProjectWorkspace>(),
            AppSettings.Shared, services.GetRequiredService<ProjectFileService>());

        // From here on, the window is put out of the way rather than closed, and the icon in the
        // notification area is how it comes back and how the program is stopped. The window is
        // given the icon as well, because the icon's menu is the one close that really leaves —
        // and that is the one time the window has to ask about work that is not saved.
        var tray = AppTray.Attach(window, desktop, () => new WindowsTray(
            Strings.Get("Tray.Tooltip"), Strings.Get("Tray.Show"), Strings.Get("Tray.Exit")));
        window.Tray = tray;

        // The list starts empty, so if the last run was stopped without saving, the work it was
        // holding is offered back here — once the window is up, because it is a dialog. The window
        // asks about it only once in the whole run: it is announced again every time it comes back
        // from the notification area, and that is not the last run.
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => _ = window.OfferRecoveryAsync());

        // The door a client's own process knocks on. Tools run here, on the project this window is
        // showing, so a draft is a copy of what is on screen and a save writes the file the person
        // has open. Permissions are read from the same settings file the flyout writes.
        McpControl.Stored = () => McpGrants.Current(AppSettings.Shared);
        McpPipe.Serve(async pipe =>
        {
            var host = AppServices.Host(services);
            host.Start(pipe, pipe);
            await host.WaitForShutdownAsync().ConfigureAwait(false);
            await host.DisposeAsync().ConfigureAwait(false);
        });

        // A second launch asks this one to come back instead of starting a copy of its own. The work
        // is done on the interface thread and this call comes back only once it is done, because the
        // launch that asked waits for that answer before it ends — and it is the one holding the
        // right to take the foreground, so an answer given too early is an answer given too late.
        SingleInstance.Listen(() => Dispatcher.UIThread.Invoke(() =>
        {
            tray?.ComeBack();
            WindowFront.Bring(window);
        }));

        var exited = false;
        desktop.Exit += (_, _) =>
        {
            // Closing the last window can end the lifetime on its own, and the way out asks for a
            // shutdown as well: the event arrives twice, and the second time the container is gone.
            if (exited)
            {
                return;
            }

            exited = true;
            services.GetRequiredService<PersistedVariables>().Dispose();
            tray?.Dispose();

            // The trigger service the container owns watches the machine through the device layer,
            // so it has to stop before that layer is let go. It is disposed here, and the device
            // layer after it, because that is the order the two depend on each other in.
            try
            {
                services.Dispose();
            }
            catch
            {
                // A service that will not let go must not stop the exit before Program takes the
                // virtual keyboard and mouse off the machine.
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
        };
    }
}
