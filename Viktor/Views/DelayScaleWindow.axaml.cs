using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Viktor.Models;
using Viktor.ViewModels;

namespace Viktor.Views;

/// <summary>
/// The small window that decides how fast this macro runs. It never changes the steps; the
/// answer it hands back is kept on the macro and used while the macro runs.
/// </summary>
public partial class DelayScaleWindow : Window
{
    public DelayScaleWindow()
        : this(new DelayScaleViewModel())
    {
    }

    public DelayScaleWindow(DelayScaleViewModel viewModel)
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
    /// Asks for the run speed over <paramref name="owner"/> and reports it, or null when the
    /// dialog was dismissed without a choice.
    /// </summary>
    public static Task<double?> ShowFor(Window owner, double current, IReadOnlyList<MacroStep> steps)
        => new DelayScaleWindow(new DelayScaleViewModel(current, steps)).ShowDialogOver<double?>(owner);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || DataContext is not DelayScaleViewModel viewModel)
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
