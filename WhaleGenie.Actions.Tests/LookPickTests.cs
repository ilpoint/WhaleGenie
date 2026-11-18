using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Picking on the picture the step itself saw, rather than on the reference picture it was looking
/// for: an offset is counted from the middle of what it found, and a pattern's colour is read out
/// of that same picture. Both are arithmetic nobody should be doing in their head.
/// </summary>
public partial class LookPickTests
{
    /// <summary>A small picture, filled with one colour and dotted with others where told.</summary>
    private static ImageFrame Picture(PixelColor fill, params (int X, int Y, PixelColor Colour)[] spots)
        => Picture(20, 20, fill, spots);

    private static ImageFrame Picture(int width, int height, PixelColor fill,
        params (int X, int Y, PixelColor Colour)[] spots)
    {
        var bytes = new byte[width * height * 4];
        for (var at = 0; at < bytes.Length; at += 4)
        {
            bytes[at] = fill.B;
            bytes[at + 1] = fill.G;
            bytes[at + 2] = fill.R;
            bytes[at + 3] = 255;
        }

        foreach (var (x, y, colour) in spots)
        {
            var at = ((y * width) + x) * 4;
            bytes[at] = colour.B;
            bytes[at + 1] = colour.G;
            bytes[at + 2] = colour.R;
            bytes[at + 3] = 255;
        }

        return new ImageFrame(width, height, bytes);
    }

    private static StepLook Look(ImageFrame frame, ScreenPoint origin, ImageMatch? hit)
        => new()
        {
            StepId = "p1c2",
            StepType = "vision.clickImage",
            Kind = LookKind.Template,
            Frame = frame,
            Origin = origin,
            Boxes = hit is null ? [] : [new LookBox(hit, LookRole.Hit)],
            ChosenIndex = hit is null ? 0 : 1,
        };
}
