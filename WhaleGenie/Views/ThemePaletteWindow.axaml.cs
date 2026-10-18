using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// The custom palette editor. It is shown over the settings window and applies every change to the
/// running program at once; closing it writes the palette that was being previewed.
/// </summary>
public partial class ThemePaletteWindow : Window
{
    private readonly ThemePaletteViewModel _viewModel;

    public ThemePaletteWindow()
        : this(new ThemePaletteViewModel())
    {
    }

    internal ThemePaletteWindow(ThemePaletteViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
        Title = Strings.Get("Theme.Palette.Title");

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
        {
            close.Click += (_, _) => Close();
        }

        Closed += (_, _) => _viewModel.Flush();
    }

    private void OnSelectColor(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: ThemeColorEntryViewModel entry })
        {
            _viewModel.Select(entry);
        }
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();

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
