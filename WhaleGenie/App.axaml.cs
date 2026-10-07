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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
