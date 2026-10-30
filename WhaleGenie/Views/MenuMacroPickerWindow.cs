using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
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

    internal MenuMacroPickerWindow(IReadOnlyList<MacroItem> macros, string? title = null,
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
        ThemeBrush.Assign(this, BackgroundProperty, "SurfaceWindow");

        // 外观交给 `TextBox.Field`；`Height = 36` 是"搜索框比标准字段高一档"，是布局不是外观。
        _search = new TextBox
        {
            Height = 36,
            Classes = { "Field" },
            PlaceholderText = placeholder ?? Strings.Get("Menu.PickMacroSearch"),
        };
        _search.TextChanged += (_, _) => Fill();
        _list = new ListBox
        {
            Classes = { "List" },
            ItemsSource = _macros,
            ItemTemplate = new FuncDataTemplate<MacroItem>((macro, _) =>
            {
                // The list is handed macro objects rather than strings; without this template the
                // row falls back to ToString() and shows the model's type name instead of its name.
                var name = new TextBlock
                {
                    Text = macro.Name,
                    Classes = { "Body" },
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                // `Caption` 本身就是三级灰，原来那 0.62 的透明度是把它再压暗一次——去掉。
                var preview = new TextBlock
                {
                    Text = macro.TriggerPreview,
                    Classes = { "Caption" },
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                return new StackPanel
                {
                    Spacing = 2,
                    Children = { name, preview },
                };
            }),
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
            Classes = { "Secondary" },
        };
        cancel.Click += (_, _) => Close(null);
        var choose = new Button
        {
            Content = Strings.Get("Common.Ok"),
            MinWidth = 88,
            Classes = { "Primary" },
        };
        choose.Click += (_, _) => Choose();
        // 整页内缩只写这一处：原来搜索框、列表、按钮行各写一条 `Margin`，
        // 且上下值互不一致（12,8 / 0,8 / 0,12）。
        Content = new Border
        {
            Padding = new Thickness(12),
            Child = new Grid
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
                        Children = { cancel, choose },
                    },
                },
            },
        };
        Grid.SetRow(_list, 1);
        Grid.SetRow(((Grid)((Border)Content!).Child!).Children[2], 2);
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

}
