using System;
using System.Collections.Generic;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Storage;

namespace WhaleGenie.Editing;

/// <summary>
/// Turns a picture a step names into something the page can show. The file itself is read once
/// per change and cached by its path and stamp; the page gets a data URL, never a second copy of
/// the picture. A file that is missing, too large or not a picture is reported as a reason so the
/// field can keep its text and still say why there is no preview.
/// </summary>
internal sealed class ActionImagePreviews
{
    private const long MaximumBytes = 4L * 1024 * 1024;

    private readonly Dictionary<string, (long Length, DateTime Stamp,
        EditorImagePreviewSnapshot Preview)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public EditorImagePreviewSnapshot? For(string? text, string assetFolder)
    {
        var written = (text ?? string.Empty).Trim();
        var path = ImageAssets.Resolve(written, assetFolder);
        if (path is null)
        {
            return LooksLikeFile(written)
                ? new EditorImagePreviewSnapshot(string.Empty, Path.GetFileName(written),
                    Strings.Get("Add.ImageMissing"))
                : null;
        }

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return new EditorImagePreviewSnapshot(string.Empty, file.Name,
                    Strings.Get("Add.ImageMissing"));
            }

            if (_cache.TryGetValue(path, out var cached)
                && cached.Length == file.Length && cached.Stamp == file.LastWriteTimeUtc)
            {
                return cached.Preview;
            }

            var preview = Read(file);
            _cache[path] = (file.Length, file.LastWriteTimeUtc, preview);
            return preview;
        }
        catch (Exception)
        {
            return new EditorImagePreviewSnapshot(string.Empty, Path.GetFileName(path),
                Strings.Get("Add.ImageUnreadable"));
        }
    }

    private static EditorImagePreviewSnapshot Read(FileInfo file)
    {
        if (file.Length == 0)
        {
            return new EditorImagePreviewSnapshot(string.Empty, file.Name,
                Strings.Get("Add.ImageUnreadable"));
        }

        if (file.Length > MaximumBytes)
        {
            return new EditorImagePreviewSnapshot(string.Empty, file.Name,
                Strings.Get("Add.ImageTooLarge"));
        }

        var mime = Mime(file.Extension);
        if (mime is null)
        {
            return new EditorImagePreviewSnapshot(string.Empty, file.Name,
                Strings.Get("Add.ImageUnsupported"));
        }

        var bytes = File.ReadAllBytes(file.FullName);
        return new EditorImagePreviewSnapshot(
            "data:" + mime + ";base64," + Convert.ToBase64String(bytes), file.Name, string.Empty);
    }

    private static string? Mime(string extension) => extension.ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".bmp" => "image/bmp",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => null,
    };

    private static bool LooksLikeFile(string text)
        => text.Length > 0
           && (text.Contains(Path.DirectorySeparatorChar) || text.Contains(Path.AltDirectorySeparatorChar)
               || Path.HasExtension(text));
}
