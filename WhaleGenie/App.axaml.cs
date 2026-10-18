using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Text computed in code is refreshed by walking the open windows; the indexer bindings
        // refresh themselves when the table raises its change.
        Strings.Current.LanguageChanged += LanguageRefresh.Refresh;

        ThemeController.Apply(ThemeController.Load());
        // Whether the virtual keyboard and mouse are to be put on the machine at startup was chosen
        // in the settings window and written beside the program; the engine does not read that file,
        // so it is told here, before anything can ask for driver-level input.
        SharedDevices.Current.Driver.AttachAtStart = LocalSettings.LoadDriverAttachAtStart();

        // How this machine reads the screen was chosen in the same window, and is told to the
        // engine the same way: a macro reads it before the settings window has ever been opened.
        CaptureSetup.Load();

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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainViewModel();
            var window = new MainWindow
            {
                DataContext = viewModel,
            };
            viewModel.Mcp.EnsureSaved = window.EnsureProjectSavedForMcpAsync;

            desktop.MainWindow = window;

            // From here on, the window is put out of the way rather than closed, and the icon in
            // the notification area is how it comes back and how the program is stopped. The window
            // is given the icon as well, because the icon's menu is the one close that really
            // leaves — and that is the one time the window has to ask about work that is not saved.
            window.Tray = AppTray.Attach(window, desktop, () => new WindowsTray(
                Strings.Get("Tray.Tooltip"), Strings.Get("Tray.Show"), Strings.Get("Tray.Exit")));

            // The list starts empty, so if the last run was stopped without saving, the work it
            // was holding is offered back here — once the window is up, because it is a dialog.
            // The window asks about it only once in the whole run: it is announced again every time
            // it comes back from the notification area, and that is not the last run.
            window.Opened += (_, _) => Dispatcher.UIThread.Post(() => _ = window.OfferRecoveryAsync());

            desktop.Exit += (_, _) =>
            {
                window.Tray?.Dispose();

                // The MCP server owns a Kestrel socket; it has to be gone before the process is.
                // This event runs on the UI thread, and an MCP request can be waiting for that same
                // thread while Kestrel drains it. Blocking here on the UI thread deadlocks the exit,
                // which is exactly what left a windowless WhaleGenie process behind. Stop on a
                // worker, wait only briefly, and let Program.Run's final exit cover a host that
                // still has not let go.
                if (window.DataContext is MainViewModel viewModel)
                {
                    try
                    {
                        Task.Run(() => viewModel.Mcp.StopAsync()).Wait(TimeSpan.FromSeconds(3));
                    }
                    catch
                    {
                        // The host has stopped or is unreachable; the process is leaving anyway.
                    }
                }

                // The program owns the device layer every window ran on, so this is where the
                // models it loaded and the capture sessions it opened are let go.
                SharedDevices.Release();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
