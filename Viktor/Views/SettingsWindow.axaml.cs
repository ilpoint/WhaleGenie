using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Viktor.Execution;
using Viktor.Localization;
using Viktor.ViewModels;

namespace Viktor.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        DataContext = new SettingsViewModel();
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
