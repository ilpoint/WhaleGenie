using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Views;

/// <summary>Picks one enabled macro for an empty menu cell.</summary>
public sealed class MenuMacroPickerWindow : Window
{
    private readonly ListBox _list;
    private readonly TextBox _search;
    private readonly IReadOnlyList<MacroItem> _macros;

    private MenuMacroPickerWindow(IReadOnlyList<MacroItem> macros, string? title = null,
        string? placeholder = null, bool enabledOnly = true)
    {
        _macros = [.. (enabledOnly ? macros.Where(macro => macro.IsEnabled) : macros)];
        Width = 380;
        Height = 440;
        MinWidth = 320;
        MinHeight = 360;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Title = title ?? Strings.Get("Menu.PickMacro");
        Background = Brush("AppBackdrop", Color.FromRgb(32, 32, 32));

        _search = new TextBox
        {
            Height = 36,
            Margin = new Thickness(12, 12, 12, 8),
            PlaceholderText = placeholder ?? Strings.Get("Menu.PickMacroSearch"),
        };
        _search.TextChanged += (_, _) => Fill();
        _list = new ListBox
        {
            Margin = new Thickness(12, 0, 12, 8),
            ItemsSource = _macros,
        };
        _list.DoubleTapped += (_, _) => Choose();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Choose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            MinWidth = 88,
            Height = 32,
        };
        cancel.Click += (_, _) => Close(null);
        var choose = new Button
        {
            Content = Strings.Get("Common.Ok"),
            MinWidth = 88,
            Height = 32,
        };
        choose.Click += (_, _) => Choose();
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            Children =
            {
                _search,
                _list,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(12, 0, 12, 12),
                    Children = { cancel, choose },
                },
            },
        };
        Grid.SetRow(_list, 1);
        Grid.SetRow(((Grid)Content).Children[2], 2);
        Opened += (_, _) => _search.Focus();
        Fill();
    }

    public static Task<MacroItem?> ShowAsync(Window owner, IReadOnlyList<MacroItem> macros,
        string? title = null, string? placeholder = null, bool enabledOnly = true)
        => new MenuMacroPickerWindow(macros, title, placeholder, enabledOnly)
            .ShowDialogOver<MacroItem?>(owner);

    private void Fill()
    {
        var wanted = _search.Text?.Trim() ?? string.Empty;
        _list.ItemsSource = _macros
            .Where(macro => wanted.Length == 0
                || macro.Name.Contains(wanted, System.StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (_list.ItemCount > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    private void Choose()
    {
        if (_list.SelectedItem is MacroItem macro)
        {
            Close(macro);
        }
    }

    private static IBrush Brush(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
