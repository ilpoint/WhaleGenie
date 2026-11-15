using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WhaleGenie.Core.Devices;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.Assets;

/// <summary>
/// The two things about a picture that need the interface toolkit: turning a captured frame into
/// something a control can show, and taking a picture of the screen for a picker. Everything else
/// about pictures — where they live, how they are saved and how a stored value finds its file —
/// belongs to <see cref="ImageAssets"/> in the document project.
/// </summary>
public static class BitmapImages
{
    /// <summary>Takes a rectangle off the screen and stores it as a PNG, reporting its path.</summary>
    public static string Capture(IScreenDevice screen, int x, int y, int width, int height,
        string folder)
        => ImageAssets.Save(ScreenReady.Capture(screen, x, y, width, height), folder);

    /// <summary>
    /// Copies a frame into a bitmap. The alpha byte a screen capture leaves behind is set to
    /// fully opaque, otherwise the picture would save as something invisible.
    /// </summary>
    public static WriteableBitmap ToBitmap(ImageFrame frame)
    {
        var pixels = new byte[frame.Width * frame.Height * 4];
        Buffer.BlockCopy(frame.Bgra, 0, pixels, 0, pixels.Length);
        for (var index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Marshal.Copy(pixels, row * frame.Width * 4,
                    IntPtr.Add(buffer.Address, row * buffer.RowBytes), frame.Width * 4);
            }
        }

        return bitmap;
    }
}
