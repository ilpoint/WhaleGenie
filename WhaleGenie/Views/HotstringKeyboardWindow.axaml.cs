using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;

namespace WhaleGenie.Views;

/// <summary>
/// The literal-character keyboard for one typed-string trigger. Unlike the action keyboard, its
/// caps write text, not key names, and the displayed count is the same 40-character limit the
/// recognizer enforces.
/// </summary>
public partial class HotstringKeyboardWindow : Window
{
    private sealed record CharacterKey(string Normal, string Shifted, bool Wide = false);

    private readonly StackPanel _rows;
    private readonly TextBlock _preview;
    private readonly TextBlock _count;
    private readonly List<(Button Button, CharacterKey Key)> _characterButtons = [];
    private readonly List<Button> _controlButtons = [];
    private bool _shift;
    private string _text;

    public HotstringKeyboardWindow()
        : this(string.Empty)
    {
    }

    public HotstringKeyboardWindow(string text)
    {
        InitializeComponent();
        Title = Strings.Get("HotstringKeyboard.Title");
        _text = text;
        _rows = this.FindControl<StackPanel>("KeyRows")!;
        _preview = this.FindControl<TextBlock>("PreviewText")!;
        _count = this.FindControl<TextBlock>("CountText")!;

        var close = this.FindControl<Button>("CloseButton");
        if (close is not null)
        {
            close.Click += (_, _) => Close(null);
        }

        var backspace = this.FindControl<Button>("BackspaceButton");
        if (backspace is not null)
        {
            backspace.Click += (_, _) => Backspace();
            _controlButtons.Add(backspace);
        }

        var clear = this.FindControl<Button>("ClearButton");
        if (clear is not null)
        {
            clear.Click += (_, _) => SetText(string.Empty);
            _controlButtons.Add(clear);
        }

        var confirm = this.FindControl<Button>("ConfirmButton");
        if (confirm is not null)
        {
            confirm.Click += (_, _) => Close(_text);
        }

        BuildRows();
        Refresh();
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    public string Text => _text;

    public void SetText(string text)
    {
        _text = text.Length > HotstringCharacters.MaximumLength
            ? text[..HotstringCharacters.MaximumLength]
            : text;
        Refresh();
    }

    private void BuildRows()
    {
        AddRow(
            new("`", "~"), new("1", "!"), new("2", "@"), new("3", "#"),
            new("4", "$"), new("5", "%"), new("6", "^"), new("7", "&"),
            new("8", "*"), new("9", "("), new("0", ")"), new("-", "_"),
            new("=", "+"), new("", "", true));
        AddRow(
            new("q", "Q"), new("w", "W"), new("e", "E"), new("r", "R"),
            new("t", "T"), new("y", "Y"), new("u", "U"), new("i", "I"),
            new("o", "O"), new("p", "P"), new("[", "{"), new("]", "}"),
            new("\\", "|"));
        AddRow(
            new("a", "A"), new("s", "S"), new("d", "D"), new("f", "F"),
            new("g", "G"), new("h", "H"), new("j", "J"), new("k", "K"),
            new("l", "L"), new(";", ":"), new("'", "\""), new("", "", true));
        AddRow(
            new("z", "Z"), new("x", "X"), new("c", "C"), new("v", "V"),
            new("b", "B"), new("n", "N"), new("m", "M"), new(",", "<"),
            new(".", ">"), new("/", "?"));

        var special = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 7,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var shift = new Button
        {
            Classes = { "CharKey" },
            MinWidth = 92,
            Content = Strings.Get("HotstringKeyboard.Shift"),
        };
        shift.Click += (_, _) =>
        {
            _shift = !_shift;
            shift.Classes.Set("ShiftOn", _shift);
            RefreshKeyCaps();
        };
        special.Children.Add(shift);
        AddSpecial(special, Strings.Get("HotstringKeyboard.Tab"), () => Append("\t"), 82);
        AddSpecial(special, Strings.Get("HotstringKeyboard.Space"), () => Append(" "), 250);
        AddSpecial(special, Strings.Get("HotstringKeyboard.Enter"), () => Append("\n"), 88);
        _rows.Children.Add(special);
    }

    private void AddRow(params CharacterKey[] keys)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        foreach (var key in keys)
        {
            if (key.Wide)
            {
                var backspace = new Button
                {
                    Classes = { "CharKey" },
                    MinWidth = 78,
                    Content = Strings.Get("HotstringKeyboard.Backspace"),
                };
                backspace.Click += (_, _) => Backspace();
                _controlButtons.Add(backspace);
                row.Children.Add(backspace);
                continue;
            }

            var button = new Button { Classes = { "CharKey" }, Content = key.Normal };
            button.Click += (_, _) => Append(_shift ? key.Shifted : key.Normal);
            _characterButtons.Add((button, key));
            row.Children.Add(button);
        }

        _rows.Children.Add(row);
    }

    private void AddSpecial(Panel row, string label, Action insert, double width)
    {
        var button = new Button
        {
            Classes = { "CharKey" },
            MinWidth = width,
            Content = label,
        };
        button.Click += (_, _) => insert();
        _controlButtons.Add(button);
        row.Children.Add(button);
    }

    private void Append(string text)
    {
        if (_text.Length >= HotstringCharacters.MaximumLength)
        {
            return;
        }

        var room = HotstringCharacters.MaximumLength - _text.Length;
        SetText(_text + text[..Math.Min(room, text.Length)]);
    }

    private void Backspace()
    {
        if (_text.Length > 0)
        {
            SetText(_text[..^1]);
        }
    }

    private void Refresh()
    {
        var shown = HotstringCharacters.Display(_text);
        _preview.Text = shown.Length == 0 ? "—" : shown;
        _count.Text = $"{_text.Length}/{HotstringCharacters.MaximumLength}";
        var full = _text.Length >= HotstringCharacters.MaximumLength;
        foreach (var (button, _) in _characterButtons)
        {
            button.IsEnabled = !full;
        }

        foreach (var button in _controlButtons)
        {
            button.IsEnabled = !full || button.Content is string text
                && (text == Strings.Get("HotstringKeyboard.Backspace")
                    || text == Strings.Get("HotstringKeyboard.Clear"));
        }

        RefreshKeyCaps();
    }

    private void RefreshKeyCaps()
    {
        foreach (var (button, key) in _characterButtons)
        {
            button.Content = _shift ? key.Shifted : key.Normal;
        }
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close(null);
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
