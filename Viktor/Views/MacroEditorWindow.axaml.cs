using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Viktor.ViewModels;

namespace Viktor.Views;

public partial class MacroEditorWindow : Window
{
    public MacroEditorWindow()
    {
        InitializeComponent();

        var viewModel = new MacroEditorViewModel();
        DataContext = viewModel;
        viewModel.CloseRequested += Close;

        var minimizeButton = this.FindControl<Button>("MinimizeButton");
        if (minimizeButton is not null)
        {
            minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        }

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton is not null)
        {
            closeButton.Click += (_, _) => Close(null);
        }

        // Tunnelling so the key reaches us before the focused control consumes it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is MacroEditorViewModel { IsCapturingKey: true } viewModel)
        {
            viewModel.CaptureKey(DescribeKey(e.Key));
            e.Handled = true;
        }
    }

    private static string DescribeKey(Key key) => key switch
    {
        Key.LeftCtrl or Key.RightCtrl => "Ctrl",
        Key.LeftShift or Key.RightShift => "Shift",
        Key.LeftAlt or Key.RightAlt => "Alt",
        Key.Return => "Enter",
        Key.Back => "Backspace",
        Key.Space => "Space",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        _ => key.ToString(),
    };

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
