using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Viktor.Localization;
using Viktor.Models;
using Viktor.ViewModels;
using Viktor.Execution;

namespace Viktor.Views;

public partial class VariableCenterWindow : Window
{
    public VariableCenterWindow()
        : this(null)
    {
    }

    /// <summary>Opens the Variable Center for the macros currently in the list.</summary>
    public VariableCenterWindow(IEnumerable<MacroItem>? macros)
    {
        InitializeComponent();

        var (width, height) = PrimaryScreenSize();
        DataContext = new VariableCenterViewModel(macros, width, height);
        Title = Strings.Get("Variable.Title");

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
        _ = LoadClipboardAsync();

        // Renaming a variable means typing, which must not set a macro off.
        MacroTriggerGate.Enter();
        Closed += (_, _) => MacroTriggerGate.Exit();
    }

    /// <summary>Screen size offered to the <c>sys.screenWidth</c> and <c>sys.screenHeight</c> rows.</summary>
    private (int Width, int Height) PrimaryScreenSize()
    {
        try
        {
            var screen = Screens.Primary;
            return screen is null ? (0, 0) : (screen.Bounds.Width, screen.Bounds.Height);
        }
        catch
        {
            // Not every platform exposes screen geometry before the window is shown.
            return (0, 0);
        }
    }

    /// <summary>Clipboard text cannot be read synchronously, so it is filled in afterwards.</summary>
    private async Task LoadClipboardAsync()
    {
        if (DataContext is not VariableCenterViewModel viewModel)
        {
            return;
        }

        try
        {
            var text = await Clipboard!.TryGetTextAsync();
            viewModel.SetClipboard(text ?? string.Empty);
        }
        catch
        {
            // The clipboard may be locked by another process; the row simply stays empty.
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape)
        {
            Close();
            e.Handled = true;
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
