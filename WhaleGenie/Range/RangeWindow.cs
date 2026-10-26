using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Range;

/// <summary>
/// A target to try things on: one window whose controls sit at fixed places and whose colour is
/// always the same, so a client can say "click at (60, 58) and the count goes up by one" instead of
/// hoping the desktop happens to look like something. It is a place to be wrong in, not a feature.
/// </summary>
/// <remarks>
/// Every control carries an automation id, because the point of the window is to be found again by a
/// selector. The numbers in <see cref="Facts"/> are where the controls sit inside the window; a
/// client that reads them can aim at a control without reading a picture first.
/// </remarks>
public sealed class RangeWindow : Window
{
    /// <summary>The window a range run opened, or null when this run is not a range.</summary>
    public static RangeWindow? Current { get; private set; }

    /// <summary>
    /// Where the window is, kept as plain numbers so a check on another thread can read it without
    /// touching the interface. It is refreshed as the window opens, moves and resizes.
    /// </summary>
    public static RangeBounds? Where { get; private set; }

    /// <summary>The colour of the swatch, which never changes while the window is open.</summary>
    public const string Swatch = "#3366CC";

    /// <summary>Where each control sits inside the window, in client pixels.</summary>
    public const int CountX = 40, CountY = 48, CountWidth = 140, CountHeight = 36;
    public const int FieldX = 40, FieldY = 108, FieldWidth = 240, FieldHeight = 32;
    public const int FlagX = 40, FlagY = 156, FlagWidth = 200, FlagHeight = 24;
    public const int SwatchX = 320, SwatchY = 48, SwatchWidth = 160, SwatchHeight = 96;
    public const int DangerX = 320, DangerY = 164, DangerWidth = 160, DangerHeight = 36;

    private readonly TextBlock _pressed;
    private readonly TextBox _field;
    private readonly CheckBox _flag;
    private readonly Border _swatch;

    public RangeWindow()
    {
        Title = "WhaleGenie Range";
        Width = 520;
        Height = 260;
        CanResize = false;

        // No frame, so the window's own position is the corner of the part a control sits in: a
        // client that reads (x, y) and clicks there lands where it meant to.
        WindowDecorations = WindowDecorations.None;
        Background = new SolidColorBrush(Color.Parse("#F4F6F8"));

        var canvas = new Canvas { Width = 520, Height = 260 };

        var count = Make<Button>("rangeCount", "按一下", CountX, CountY, CountWidth, CountHeight);
        count.Click += (_, _) =>
        {
            Presses++;
            Render();
        };
        canvas.Children.Add(count);

        _field = Make<TextBox>("rangeInput", string.Empty, FieldX, FieldY, FieldWidth, FieldHeight);
        _field.PlaceholderText = "在这里打字";
        _field.TextChanged += (_, _) => Render();
        canvas.Children.Add(_field);

        _flag = Make<CheckBox>("rangeFlag", "勾上我", FlagX, FlagY, FlagWidth, FlagHeight);
        _flag.IsChecked = false;
        _flag.IsCheckedChanged += (_, _) => Render();
        canvas.Children.Add(_flag);

        _swatch = Make<Border>("rangeColour", string.Empty, SwatchX, SwatchY, SwatchWidth, SwatchHeight);
        _swatch.Background = new SolidColorBrush(Color.Parse(Swatch));
        canvas.Children.Add(_swatch);

        var danger = Make<Button>("rangeDanger", "别按我", DangerX, DangerY, DangerWidth, DangerHeight);
        danger.Click += (_, _) =>
        {
            DangerPresses++;
            Render();
        };
        canvas.Children.Add(danger);

        _pressed = Make<TextBlock>("rangePresses", "按了 0 次", 40, 196, 240, 24);
        canvas.Children.Add(_pressed);

        Content = canvas;
        Render();
    }

    /// <summary>The window becomes the range as it opens, and stops being one as it closes.</summary>
    protected override void OnOpened(EventArgs e)
    {
        Current = this;
        Locate();
        PositionChanged += (_, _) => Locate();
        SizeChanged += (_, _) => Locate();
        base.OnOpened(e);
    }

    /// <summary>Once closed it is not a place to try things on any more.</summary>
    protected override void OnClosed(EventArgs e)
    {
        if (ReferenceEquals(Current, this))
        {
            Current = null;
            Where = null;
        }

        base.OnClosed(e);
    }

    /// <summary>Reads where the window is now, so the check on another thread has the latest numbers.</summary>
    private void Locate() => Where = new RangeBounds(
        (TryGetPlatformHandle()?.Handle ?? IntPtr.Zero).ToInt64(),
        Position.X,
        Position.Y,
        (int)ClientSize.Width,
        (int)ClientSize.Height);

    /// <summary>How many times the counting button was pressed since the last reset.</summary>
    public int Presses { get; private set; }

    /// <summary>How many times the button nothing should ever press was pressed.</summary>
    public int DangerPresses { get; private set; }

    /// <summary>What the text box holds.</summary>
    public string Field => _field.Text ?? string.Empty;

    /// <summary>Whether the box is ticked.</summary>
    public bool Flag => _flag.IsChecked == true;

    /// <summary>Everything the window shows, so a client can compare a run's effect with what it did.</summary>
    public object Facts()
    {
        return new
        {
            available = true,
            handle = (TryGetPlatformHandle()?.Handle ?? IntPtr.Zero).ToInt64(),
            title = Title ?? string.Empty,
            x = Position.X,
            y = Position.Y,
            width = (int)ClientSize.Width,
            height = (int)ClientSize.Height,
            folder = RangePolicy.Folder,
            presses = Presses,
            dangerPresses = DangerPresses,
            text = Field,
            ticked = Flag,
            colour = Swatch,
            targets = new[]
            {
                Target("rangeCount", CountX, CountY, CountWidth, CountHeight),
                Target("rangeInput", FieldX, FieldY, FieldWidth, FieldHeight),
                Target("rangeFlag", FlagX, FlagY, FlagWidth, FlagHeight),
                Target("rangeColour", SwatchX, SwatchY, SwatchWidth, SwatchHeight),
                Target("rangeDanger", DangerX, DangerY, DangerWidth, DangerHeight),
            },
        };
    }

    /// <summary>Puts the window back the way it started, which is what makes two runs comparable.</summary>
    public void Reset()
    {
        Presses = 0;
        DangerPresses = 0;
        _field.Text = string.Empty;
        _flag.IsChecked = false;
        _swatch.Background = new SolidColorBrush(Color.Parse(Swatch));
        Render();
    }

    /// <summary>Where one control sits inside the window, and where that is on the screen.</summary>
    private static object Target(string id, int x, int y, int width, int height) => new
    {
        id,
        x,
        y,
        width,
        height,
    };

    /// <summary>Keeps the label that says how many presses there were in step with the count.</summary>
    private void Render()
        => _pressed.Text = $"按了 {Presses} 次；别按的 {DangerPresses} 次";

    /// <summary>One control at the place it belongs, with the id a selector finds it by.</summary>
    private static T Make<T>(string id, string text, int x, int y, int width, int height)
        where T : Control, new()
    {
        var control = new T
        {
            Width = width,
            Height = height,
        };

        if (control is ContentControl content)
        {
            content.Content = text;
        }
        else if (control is TextBlock block)
        {
            block.Text = text;
            block.VerticalAlignment = VerticalAlignment.Center;
        }

        AutomationProperties.SetAutomationId(control, id);
        AutomationProperties.SetName(control, id);
        Canvas.SetLeft(control, x);
        Canvas.SetTop(control, y);
        return control;
    }
}
