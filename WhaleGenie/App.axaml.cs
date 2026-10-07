using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Execution;
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
        // Where this machine's viiper.exe is was chosen in the settings window and written beside
        // the program; the engine does not read that file, so it is told here, before anything can
        // ask for driver-level input.
        ViiperSetup.Load();

        // And then it is given a head start: starting that server is what makes the first
        // driver-level move of a macro wait a second and a half.
        _ = ViiperServer.WarmUp();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = new MainViewModel(),
            };

            desktop.MainWindow = window;

            // From here on, the window is put out of the way rather than closed, and the icon in
            // the notification area is how it comes back and how the program is stopped. The window
            // is given the icon as well, because the icon's menu is the one close that really
            // leaves — and that is the one time the window has to ask about work that is not saved.
            window.Tray = AppTray.Attach(window, desktop);
            desktop.Exit += (_, _) =>
            {
                window.Tray?.Dispose();
                BrowserPicker.Close();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
