using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Viktor.Core.Devices;

namespace Viktor.Storage;

/// <summary>
/// The pictures a macro looks for on screen: where a new one is put, how a rectangle of the
/// screen becomes one, and which file a stored value points at.
/// </summary>
public static class ImageAssets
{
    /// <summary>
    /// Where a freshly taken picture goes: beside the macro package, so it travels with it,
    /// or in WhaleGenie's own folder while the project has not been saved yet.
    /// </summary>
    public static string FolderFor(string? packagePath)
        => string.IsNullOrWhiteSpace(packagePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WhaleGenie",
                "images")
            : MacroPackage.AssetFolderFor(packagePath!);

    /// <summary>Takes a rectangle off the screen and stores it as a PNG, reporting its path.</summary>
    public static string Capture(IScreenDevice screen, int x, int y, int width, int height, string folder)
        => Save(screen.Capture(x, y, width, height), folder);

    /// <summary>Writes a block of pixels out as an opaque PNG and reports the file it made.</summary>
    public static string Save(ImageFrame frame, string folder)
    {
        if (frame.IsEmpty)
        {
            throw new ArgumentException("There are no pixels to save.", nameof(frame));
        }

        Directory.CreateDirectory(folder);
        var path = UniquePath(folder, Name());

        using (var bitmap = ToBitmap(frame))
        using (var stream = File.Create(path))
        {
            bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        }

        return path;
    }

    /// <summary>
    /// The file a stored picture value points at, or null when there is nothing to show. An
    /// absolute path is used as it stands; anything else is looked for beside the package.
    /// </summary>
    public static string? Resolve(string? value, string folder)
    {
        var text = (value ?? string.Empty).Trim().Trim('"');
        if (text.Length == 0)
        {
            return null;
        }

        if (Path.IsPathRooted(text))
        {
            return File.Exists(text) ? text : null;
        }

        var root = string.IsNullOrEmpty(folder) ? "." : folder;
        var candidate = Path.Combine(root, text.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    /// <summary>
    /// Copies a frame into a bitmap. The alpha byte a screen capture leaves behind is set to
    /// fully opaque, otherwise the picture would save as something invisible.
    /// </summary>
    private static WriteableBitmap ToBitmap(ImageFrame frame)
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

    /// <summary>A name that says when the picture was taken, which reads better in a folder listing.</summary>
    private static string Name()
        => "screen-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

    private static string UniquePath(string folder, string name)
    {
        var candidate = Path.Combine(folder, name + ".png");
        var counter = 1;

        while (File.Exists(candidate))
        {
            counter++;
            candidate = Path.Combine(folder, string.Create(CultureInfo.InvariantCulture,
                $"{name}-{counter}.png"));
        }

        return candidate;
    }
}
