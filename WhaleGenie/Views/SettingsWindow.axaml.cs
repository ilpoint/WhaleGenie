using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        DataContext = new SettingsViewModel(SharedDevices.Current.Driver);
        Title = Strings.Get("Settings.Title");

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close();
        }

        var doneButton = this.FindControl<Button>("DoneButton");
        if (doneButton is not null)
        {
            doneButton.Click += (_, _) => Close();
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        // Editing settings means typing, which must not set a macro off.
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();
    }

    /// <summary>Opens the palette editor over the settings window, keeping this window as its owner.</summary>
    private async void OnCustomizeThemeClicked(object? sender, RoutedEventArgs e)
        => await new ThemePaletteWindow().ShowDialogOver(this);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Sends the user to the driver's own download page instead of installing anything: the driver
    /// belongs to another project, its installer wants administrator rights and a restart, and
    /// fetching a file is something the person should see happen.
    /// </summary>
    private void OnInstallDriverClicked(object? sender, RoutedEventArgs e)
        => WebPage.Open(ProjectLinks.DriverDownload);

    /// <summary>
    /// Starts a second copy of WhaleGenie with the UAC prompt in front of it, and closes this one.
    ///
    /// This is the only way to get the rights: a process cannot be given an administrator token
    /// after it has started, and hotkeys are read through a system hook that Windows does not let a
    /// lesser process put in front of a higher-privileged window. Two copies at once would both be
    /// listening for those hotkeys, so one of them has to go — this one.
    /// </summary>
    private void OnRestartElevatedClicked(object? sender, RoutedEventArgs e)
    {
        // The virtual devices go first: the copy that takes over puts on its own, and a pair left
        // over from this one would be taken off the machine on the way out, right under the new
        // copy. It is a hand-over rather than the way out, because saying no at the UAC prompt
        // leaves this copy running.
        SharedDevices.Current.Driver.HandOver();

        if (!ProcessRights.RestartElevated())
        {
            return;
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && e.GetPosition(this).Y <= 38)
        {
            BeginMoveDrag(e);
        }
    }
}
