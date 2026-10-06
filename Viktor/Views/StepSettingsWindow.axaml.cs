using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Viktor.Core.Execution;
using Viktor.ViewModels;

namespace Viktor.Views;

/// <summary>
/// The small window that decides how one step behaves when it is slow or fails: the note under it,
/// how long it may take, how often it is tried again, how the pause between attempts grows, and
/// what happens when it finally fails. It hands the settings back rather than changing the step.
/// </summary>
public partial class StepSettingsWindow : Window
{
    public StepSettingsWindow()
        : this(new StepSettingsViewModel())
    {
    }

    public StepSettingsWindow(StepSettingsViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.CloseRequested += choice => Close(choice);

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close(null);
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Asks for the settings of one step over <paramref name="owner"/> and reports what was
    /// chosen, or null when the dialog was dismissed without a choice.
    /// </summary>
    public static Task<StepMeta?> ShowFor(Window owner, StepMeta meta)
        => new StepSettingsWindow(new StepSettingsViewModel(meta)).ShowDialog<StepMeta?>(owner);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || DataContext is not StepSettingsViewModel viewModel)
        {
            return;
        }

        viewModel.CancelCommand.Execute(null);
        e.Handled = true;
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
