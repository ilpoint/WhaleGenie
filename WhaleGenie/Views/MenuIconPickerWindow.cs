using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Views;

/// <summary>Chooses one built-in line icon or a PNG/JPEG/BMP image for a menu cell.</summary>
public sealed class MenuIconPickerWindow : Window
{
    private MenuIconPickerWindow()
    {
        Width = 560;
        Height = 430;
        MinWidth = 460;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Title = Strings.Get("Menu.PickIcon");
        Background = Brush("AppBackdrop", Color.FromRgb(32, 32, 32));

        var icons = new UniformGrid
        {
            Columns = 7,
            Margin = new Thickness(12, 12, 12, 8),
        };
        foreach (var option in MenuIconCatalog.Options)
        {
            var button = new Button
            {
                Width = 62,
                Height = 62,
                Margin = new Thickness(3),
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(5),
                Content = new StackPanel
                {
                    Spacing = 3,
                    Children =
                    {
                        new IconView
                        {
                            Width = 24,
                            Height = 24,
                            Data = option.Geometry,
                            Stroke = Brush("AppText", Colors.White),
                            StrokeThickness = 1.5,
                            HorizontalAlignment = HorizontalAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = option.Label,
                            FontSize = 9.5,
                            TextAlignment = TextAlignment.Center,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        },
                    },
                },
            };
            ToolTip.SetTip(button, option.Label);
            button.Click += (_, _) =>
                Close(new MenuIconReference(MenuIconKind.Builtin, option.Key));
            icons.Children.Add(button);
        }

        var image = new Button
        {
            Content = Strings.Get("Menu.PickImage"),
            MinWidth = 120,
            Height = 32,
        };
        image.Click += async (_, _) => await PickImageAsync();
        var cancel = new Button
        {
            Content = Strings.Get("Common.Cancel"),
            MinWidth = 88,
            Height = 32,
        };
        cancel.Click += (_, _) => Close(null);
        Content = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Children =
            {
                new ScrollViewer { Content = icons },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Margin = new Thickness(12, 0, 12, 12),
                    Children = { image, cancel },
                },
            },
        };
        Grid.SetRow(((Grid)Content).Children[1], 1);
    }

    public static Task<MenuIconReference?> ShowAsync(Window owner)
        => new MenuIconPickerWindow().ShowDialogOver<MenuIconReference?>(owner);

    private async Task PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Strings.Get("Menu.PickImage"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Strings.Get("Menu.ImageFiles"))
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/bmp"],
                },
            ],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { Length: > 0 } path)
        {
            Close(new MenuIconReference(MenuIconKind.Image, path));
        }
    }

    private static IBrush Brush(string key, Color fallback)
        => Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
