using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// Pictures: taking one and finding one on screen.
/// <summary>Looking for a picture on screen.</summary>
public interface IVisionDevice
{
    /// <summary>Reads a reference image from disk, or null when the file cannot be read.</summary>
    ImageFrame? Load(string path);

    /// <summary>
    /// Every place <paramref name="needle"/> appears in <paramref name="haystack"/>, surest first
    /// and at most <see cref="VisionQuery.Limit"/> of them, looked for the way the query asks. Two
    /// hits closer together than the reference picture count as one, because they are the same
    /// thing seen twice. Looking by features answers with one place at most: what those pairs say
    /// is where the whole of it landed, not where every one of them is.
    /// </summary>
    IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle, VisionQuery query);
}
