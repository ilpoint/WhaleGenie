#if DEBUG
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;

namespace WhaleGenie.Views;

/// <summary>
/// A throwaway window for looking at layer one with human eyes: six bare controls and one opened
/// drop-down, nothing of ours around them. It exists because the layer-one keys are the one part of
/// the palette whose failure is silent — the controls simply paint the stock Fluent colours and no
/// test that reads a token would notice — so somebody has to look at the states at least once per
/// change to the theme.
///
/// Debug only, and it goes away with the branch in <c>Program</c> once the layer has been reviewed.
/// </summary>
internal sealed class ThemeProbeWindow : Window
{
    public ThemeProbeWindow()
    {
        Title = "theme probe";
        Width = 560;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var column = new StackPanel { Spacing = 12, Margin = new Thickness(16) };
        column.Children.Add(Label("Button — rest / disabled"));
        column.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new Button { Content = "Button" },
                new Button { Content = "Disabled", IsEnabled = false },
                new ToggleButton { Content = "Toggle", IsChecked = true },
            },
        });

        column.Children.Add(Label("TextBox — rest / disabled / placeholder"));
        column.Children.Add(new TextBox { Text = "TextBox", Width = 320 });
        column.Children.Add(new TextBox { Text = "disabled", IsEnabled = false, Width = 320 });
        column.Children.Add(new TextBox { PlaceholderText = "placeholder", Width = 320 });

        column.Children.Add(Label("ComboBox — rest / opened"));
        column.Children.Add(new ComboBox
        {
            Width = 320,
            ItemsSource = new[] { "first", "second", "third" },
            SelectedIndex = 0,
        });

        column.Children.Add(Label("ScrollBar / ListBox"));
        column.Children.Add(new ListBox
        {
            Height = 96,
            ItemsSource = Enumerable.Range(1, 20).Select(number => "row " + number).ToList(),
        });

        column.Children.Add(Label("TableView"));
        column.Children.Add(new TextBox { Text = "see the note in the commit: no header key", IsReadOnly = true });

        column.Children.Add(Label("CheckBox / ToggleSwitch"));
        column.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Children =
            {
                new CheckBox { Content = "off" },
                new CheckBox { Content = "on", IsChecked = true },
                new ToggleSwitch { OnContent = "on", IsChecked = true },
            },
        });

        Content = new ScrollViewer { Content = column };
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = new SolidColorBrush(Colors.Gray),
    };

    /// <summary>
    /// Stands the probe in for the main window. Nothing of the ordinary start-up runs with it: no
    /// devices, no project, no recovery snapshot, so a look at the colours cannot touch the machine.
    /// </summary>
    internal static void Show(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var window = new ThemeProbeWindow();
        desktop.MainWindow = window;
        window.Show();
    }
}
#endif
