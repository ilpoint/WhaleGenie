using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// Lists the windows that are open so one can be pointed at by name. A macro matches a window
/// by part of its title, so the picker handles the exact title over and leaves it editable.
/// </summary>
public partial class WindowPickerWindow : Window
{
    private bool _done;

    public WindowPickerWindow()
        : this(new WindowPickerViewModel())
    {
    }

    /// <summary>Opens the picker over a list of windows, which is how the tests drive it.</summary>
    internal WindowPickerWindow(WindowPickerViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        Title = viewModel.Header;

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => OnCloseRequested(null);
        }

        // Double-clicking a row is the fastest way through a list this short.
        var list = this.FindControl<ListBox>("WindowList");
        if (list is not null)
        {
            list.DoubleTapped += (_, _) => Confirm();
        }

        // Tunnelling, so Enter and Escape are caught wherever the focus is.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The title the picker was closed with, or null when nothing was taken.</summary>
    internal string? Chosen { get; private set; }

    /// <summary>Shows the picker over <paramref name="owner"/> and reports the title that was picked.</summary>
    public static Task<string?> PickAsync(Window owner)
        => new WindowPickerWindow().ShowDialogOver<string?>(owner);

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                OnCloseRequested(null);
                break;

            case Key.Enter:
                e.Handled = true;
                Confirm();
                break;
        }
    }

    private void Confirm()
    {
        if (DataContext is WindowPickerViewModel viewModel
            && viewModel.ChooseCommand.CanExecute(null))
        {
            viewModel.ChooseCommand.Execute(null);
        }
    }

    private void OnCloseRequested(string? title)
    {
        if (_done)
        {
            return;
        }

        _done = true;
        Chosen = title;
        Close(title);
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
