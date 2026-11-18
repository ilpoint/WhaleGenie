using System;
using System.Collections.Generic;
using System.IO;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Views;

/// <summary>
/// Builds the control that shows one menu or wheel icon, and keeps an image file loaded for as long
/// as the view it belongs to lives. A wheel redraws on every change of direction, so reading the
/// same picture from disk each time would make a hover cost more than the drawing itself.
/// </summary>
internal sealed class MenuIconPicture : IDisposable
{
    private readonly Dictionary<string, Bitmap?> _pictures =
        new(StringComparer.OrdinalIgnoreCase);

    public Control? Create(MenuIconReference? icon, double size, IBrush ink, double stroke = 1.5)
    {
        if (icon is not { IsEmpty: false })
        {
            return null;
        }

        if (icon.Kind == MenuIconKind.Builtin && MenuIconCatalog.Find(icon.Value) is { } option)
        {
            return new IconView
            {
                Width = size,
                Height = size,
                Data = option.Kind,
                Stroke = ink,
                StrokeThickness = stroke,
            };
        }

        if (icon.Kind == MenuIconKind.Image && Load(icon.Value) is { } bitmap)
        {
            return new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Source = bitmap,
            };
        }

        return null;
    }

    private Bitmap? Load(string path)
    {
        if (_pictures.TryGetValue(path, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            if (File.Exists(path))
            {
                bitmap = new Bitmap(path);
            }
        }
        catch
        {
            // A moved or unreadable picture falls back to the label, which is what the wheel shows
            // when there is no icon at all.
        }

        _pictures[path] = bitmap;
        return bitmap;
    }

    public void Dispose()
    {
        foreach (var bitmap in _pictures.Values)
        {
            bitmap?.Dispose();
        }

        _pictures.Clear();
    }
}
