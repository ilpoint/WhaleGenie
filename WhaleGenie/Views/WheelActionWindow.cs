using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Views;

/// <summary>
/// Edits one position of the wheel: the macro it runs, the note shown in its place and its icon.
/// </summary>
/// <remarks>
/// The position is handed in as a copy and comes back as one, so Cancel means what it says. In a
/// program scene an inherited position arrives already carrying the global action; saving it writes
/// that action into the program scene, which is exactly the override the wheel draws.
/// </remarks>
public sealed class WheelActionWindow : AppWindow
{
    private readonly WheelSlot _slot;
    private readonly IReadOnlyList<MacroItem> _macros;
    private readonly string? _projectPath;
    private readonly MenuIconPicture _icons = new();
    private readonly MenuIconReference? _originalIcon;
    private readonly TextBlock _macroName;
    private readonly TextBox _note;
    private readonly ContentControl _iconPreview;
    private readonly TextBlock _iconName;
    private MenuIconReference? _icon;

    internal WheelActionWindow(WheelSlot slot, IReadOnlyList<MacroItem> macros,
        string position, bool inherited, string? projectPath = null)
    {
        _slot = slot.Copy();
        _icon = _slot.Icon;
        _originalIcon = _slot.Icon;
        _macros = macros;
        _projectPath = projectPath;

        Width = 470;
        Height = 400;
        MinWidth = 420;
        MinHeight = 320;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Title = Strings.Get("Wheel.ActionTitle");
        ThemeBrush.Assign(this, BackgroundProperty, "SurfaceWindow");

        var heading = new TextBlock
        {
            Text = Strings.Get("Wheel.ActionTitle"),
            Classes = { "Title" },
        };
        var where = new TextBlock
        {
            Text = position,
            Classes = { "Hint" },
        };

        _macroName = new TextBlock
        {
            Name = "ActionMacroName",
            Text = _slot.Macro.Length > 0 ? _slot.Macro : Strings.Get("Common.None"),
            Classes = { "Body" },
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.Wrap,
        };
        var chooseMacro = SmallButton("Wheel.ActionChooseMacro", OnChooseMacro);
        var clearMacro = SmallButton("Wheel.ActionClearMacro", OnClearMacro);

        _note = new TextBox
        {
            Name = "ActionNote",
            Classes = { "Field" },
            Text = _slot.Note,
            PlaceholderText = Strings.Get("Wheel.ActionNotePlaceholder"),
        };

        _iconPreview = new ContentControl
        {
            Width = 26,
            Height = 26,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _iconName = new TextBlock
        {
            Classes = { "Hint" },
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var iconBox = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(5),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush.Of("BorderControl"),
            Background = ThemeBrush.Of("SurfaceField"),
            Child = _iconPreview,
        };
        var pickIcon = SmallButton("Wheel.ActionPickIcon", OnPickIcon);
        var clearIcon = SmallButton("Wheel.ActionClearIcon", OnClearIcon);

        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            Classes = { "Secondary" },
            Height = 32,
            MinWidth = 88,
        };
        cancel.Click += (_, _) => Close(null);

        var clear = new Button
        {
            Content = Strings.Get("Wheel.ActionClearSlot"),
            Classes = { "Secondary" },
            Height = 32,
            MinWidth = 88,
        };
        clear.Click += (_, _) => Close(new WheelSlot());

        // An empty program position inherits the global action again, so "clear" there would look
        // like a button that does nothing. Removing a global action belongs to the global scene.
        clear.IsVisible = !inherited;

        var ok = new Button
        {
            Content = Strings.Get("Common.Ok"),
            Classes = { "Primary" },
            Height = 32,
            MinWidth = 88,
        };
        ok.Click += (_, _) =>
        {
            AdoptIcon();
            Close(_slot);
        };

        var body = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                where,
            },
        };
        if (inherited)
        {
            body.Children.Add(new TextBlock
            {
                Text = Strings.Get("Wheel.ActionInherited"),
                Classes = { "Hint" },
                Foreground = ThemeBrush.Of("Warning"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 4),
            });
        }

        body.Children.Add(Label("Wheel.ActionMacro"));
        body.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            ColumnSpacing = 6,
            Children =
            {
                _macroName,
                WithColumn(chooseMacro, 1),
                WithColumn(clearMacro, 2),
            },
        });
        body.Children.Add(Label("Wheel.ActionNote"));
        body.Children.Add(_note);
        body.Children.Add(Label("Wheel.ActionIcon"));
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            Children = { iconBox, _iconName, pickIcon, clearIcon },
        });
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { clear, cancel, ok },
        };

        Content = new Border
        {
            Padding = new Thickness(16),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,*,Auto"),
                RowSpacing = 8,
                Children =
                {
                    new StackPanel { Spacing = 1, Children = { heading } },
                    new ScrollViewer { Content = body },
                    buttons,
                },
            },
        };
        Grid.SetRow(((Grid)((Border)Content).Child!).Children[1], 1);
        Grid.SetRow(buttons, 2);

        Closed += (_, _) => _icons.Dispose();
        ShowIcon();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
                e.Handled = true;
            }
        };
    }

    /// <summary>Opens the editor over the settings window and hands back the action or null.</summary>
    public static Task<WheelSlot?> ShowAsync(Window owner, WheelSlot slot,
        IReadOnlyList<MacroItem> macros, string position, bool inherited,
        string? projectPath = null)
        => new WheelActionWindow(slot, macros, position, inherited, projectPath)
            .ShowDialogOver<WheelSlot?>(owner);

    private static TextBlock Label(string key) => new()
    {
        Text = Strings.Get(key),
        Classes = { "FieldLabel" },
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static Button SmallButton(string key,
        EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var button = new Button
        {
            Content = Strings.Get(key),
            Classes = { "Secondary" },
            Height = 28,
            Padding = new Thickness(9, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += click;
        return button;
    }

    private static Control WithColumn(Control control, int column)
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private async void OnChooseMacro(object? sender,
        Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (await MenuMacroPickerWindow.ShowAsync(this, _macros) is { } macro)
        {
            _slot.Macro = macro.Name.Trim();
            _macroName.Text = _slot.Macro;
        }
    }

    private void OnClearMacro(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _slot.Macro = string.Empty;
        _macroName.Text = Strings.Get("Common.None");
    }

    private async void OnPickIcon(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (await MenuIconPickerWindow.ShowAsync(this) is { } icon)
        {
            _icon = icon.IsEmpty ? null : icon;
            ShowIcon();
        }
    }

    private void OnClearIcon(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _icon = null;
        ShowIcon();
    }

    /// <summary>
    /// Copies a chosen picture beside the project only when the dialog is confirmed: a cancelled
    /// edit must not leave a file nothing points at.
    /// </summary>
    private void AdoptIcon()
    {
        if (_slot.Icon is not { IsEmpty: false, Kind: MenuIconKind.Image } icon
            || Equals(_slot.Icon, _originalIcon))
        {
            return;
        }

        try
        {
            _slot.Icon = new MenuIconReference(icon.Kind,
                ImageAssets.Adopt(icon.Value, _projectPath));
        }
        catch
        {
            // A picture that cannot be copied stays where it was picked from; the wheel still
            // draws it while the file is there.
        }
    }

    private void ShowIcon()
    {
        _iconPreview.Content = _icons.Create(_icon, 18,
            ThemeBrush.Of("TextPrimary") ?? Avalonia.Media.Brushes.Black);
        _iconName.Text = _icon switch
        {
            { IsEmpty: false, Kind: MenuIconKind.Builtin } icon => icon.Value,
            { IsEmpty: false, Kind: MenuIconKind.Image } image =>
                System.IO.Path.GetFileName(image.Value),
            _ => Strings.Get("Common.None"),
        };
        _slot.Icon = _icon;
    }
}
