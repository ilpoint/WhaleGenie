using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// Text, pictures and files on the clipboard.
/// <summary>The clipboard: the text something last copied, and a way to put text there.</summary>
public interface IClipboardDevice
{
    /// <summary>
    /// A number that goes up whenever the clipboard changes. A macro waits for the next
    /// copy by remembering this and watching for it to move.
    /// </summary>
    int ChangeCount { get; }

    /// <summary>Whether there is text on the clipboard.</summary>
    bool HasText { get; }

    /// <summary>The text on the clipboard, or an empty string when there is none.</summary>
    string ReadText();

    /// <summary>
    /// The picture on the clipboard, or null when there is not one. A picture copied from the
    /// screen comes back the size it was copied at.
    /// </summary>
    ImageFrame? ReadImage();

    /// <summary>Replaces whatever is on the clipboard with this text.</summary>
    void WriteText(string text);

    /// <summary>Puts a picture on the clipboard, the same as copying one.</summary>
    void WriteImage(ImageFrame image);

    /// <summary>The paths of the files on the clipboard, or an empty list when there are none.</summary>
    IReadOnlyList<string> ReadFiles();

    /// <summary>
    /// Puts file paths on the clipboard the way copying files in Explorer does, so pasting drops
    /// the files themselves rather than their names.
    /// </summary>
    void WriteFiles(IReadOnlyList<string> paths);

    /// <summary>Empties the clipboard.</summary>
    void Clear();
}
