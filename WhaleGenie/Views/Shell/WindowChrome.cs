using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace WhaleGenie.Views;

/// <summary>
/// The strip at the top of a window: the whale, the window's name, and the room the caption buttons
/// need. Dragging it is <see cref="AppWindow"/>'s work, which only begins once the title bar is
/// handed over to the content — a window that leaves the bar to the library draws the library's own,
/// with no whale on it and nothing to grab above the content.
/// </summary>
internal static class WindowChrome
{
    /// <summary>How tall the strip is, which is also how far down the window can be grabbed.</summary>
    private const double BarHeight = 40;

    /// <summary>
    /// The width the system's own minimize, maximize and close buttons take at 100% scaling. The
    /// library draws them over the content, so this much of the strip has to be left empty.
    /// </summary>
    private const double CaptionWidth = 138;

    private const string ChromeName = "WhaleChrome";

    /// <summary>
    /// Hands the title bar over to the content and puts the whale and the window's name in it.
    /// </summary>
    /// <remarks>
    /// The name follows <see cref="Window.Title"/> rather than being copied once: a window whose
    /// heading changes as it is used — the expression builder names what it is building — would
    /// otherwise keep the first one for ever.
    /// </remarks>
    public static void Attach(AppWindow window, Control? accessory = null)
    {
        if (window.Content is not Control content
            || content is Grid { Name: ChromeName })
        {
            // Already wearing it: a window with a designer constructor and a working one would
            // otherwise be wrapped twice, and the second strip would have nothing left to drag.
            return;
        }

        window.TitleBar.ExtendsContentIntoTitleBar = true;
        window.TitleBar.Height = BarHeight;

        if (window.Icon is null)
        {
            window.Icon = new Bitmap(
                AssetLoader.Open(new Uri("avares://WhaleGenie/Assets/WhaleGenie.ico")));
        }

        var bar = BuildBar(window, accessory);
        var layout = new Grid
        {
            Name = ChromeName,
            RowDefinitions = new RowDefinitions($"{BarHeight},*"),
        };

        // Let go of the content before it moves: a control still parented by the window cannot be
        // added to the layout, and Avalonia says so by name.
        window.Content = null;

        layout.Children.Add(bar);
        Grid.SetRow(content, 1);
        layout.Children.Add(content);
        window.Content = layout;
    }

    private static Grid BuildBar(AppWindow window, Control? accessory)
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions($"Auto,*,Auto,{CaptionWidth}"),
        };

        ThemeBrush.Assign(bar, Panel.BackgroundProperty, "SurfaceChrome");

        var icon = new Image
        {
            Source = new Bitmap(AssetLoader.Open(new Uri("avares://WhaleGenie/Assets/WhaleGenie.png"))),
            Width = 22,
            Height = 22,
            Margin = new Thickness(12, 0, 8, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        Grid.SetColumn(icon, 0);
        bar.Children.Add(icon);

        var title = new TextBlock
        {
            Text = window.Title ?? string.Empty,
            FontSize = 13,
            Margin = new Thickness(0, 0, 12, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        ThemeBrush.Assign(title, TextBlock.ForegroundProperty, "TextPrimary");
        Grid.SetColumn(title, 1);
        bar.Children.Add(title);

        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.TitleProperty)
            {
                title.Text = window.Title ?? string.Empty;
            }
        };

        if (accessory is not null)
        {
            accessory.Margin = new Thickness(0, 0, 8, 0);
            accessory.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetColumn(accessory, 2);
            bar.Children.Add(accessory);
        }

        return bar;
    }
}
