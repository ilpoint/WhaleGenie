using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Viktor.ViewModels;

namespace Viktor.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
        : this(new ConfirmDialogViewModel())
    {
    }

    public ConfirmDialog(ConfirmDialogViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.CloseRequested += choice => Close(choice);

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close(ConfirmChoice.Cancel);
        }

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Shows the dialog over <paramref name="owner"/> and reports the choice.</summary>
    public static Task<ConfirmChoice> ShowAsync(Window owner, string header, string message,
        string primaryLabel, string? secondaryLabel = null, bool showCancel = true,
        string? cancelLabel = null)
    {
        var dialog = new ConfirmDialog(new ConfirmDialogViewModel
        {
            Header = header,
            Message = message,
            PrimaryLabel = primaryLabel,
            SecondaryLabel = secondaryLabel,
            ShowCancel = showCancel,
            CancelLabel = cancelLabel,
        });

        return dialog.ShowDialogOver<ConfirmChoice>(owner);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Escape || DataContext is not ConfirmDialogViewModel viewModel)
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
