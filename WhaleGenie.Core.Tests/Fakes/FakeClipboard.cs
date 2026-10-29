using System.Collections.Generic;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend clipboard: a line of text, some files and a picture, with a change counter a step can
/// watch.
/// </summary>
/// <remarks>
/// <see cref="MovesAfter"/> is what makes a waiting step interesting: the counter starts moving on
/// its own after that many reads, standing in for another program copying something while the step
/// looks.
/// </remarks>
internal sealed class FakeClipboard(FakeDesk desk) : IClipboardDevice
{
    /// <summary>What is on the pretend clipboard.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The file paths the fake clipboard is holding.</summary>
    public List<string> Files { get; } = [];

    /// <summary>The picture the fake clipboard is holding, if any.</summary>
    public ImageFrame? Picture { get; set; }

    /// <summary>Goes up whenever the pretend clipboard changes.</summary>
    public int Changes { get; private set; }

    /// <summary>
    /// How many times the change counter may be read before it starts moving by itself, which
    /// stands in for another program copying something while a step waits.
    /// </summary>
    public int MovesAfter { get; set; } = int.MaxValue;

    private int _reads;

    public int ChangeCount
    {
        get
        {
            _reads++;
            if (_reads > MovesAfter)
            {
                Changes++;
            }

            return Changes;
        }
    }

    public bool HasText => !string.IsNullOrEmpty(Text);

    public string ReadText()
    {
        desk.Note("clipboardRead");
        return Text;
    }

    public void WriteText(string text)
    {
        desk.Note($"clipboardWrite {text}");
        Text = text;
        Changes++;
    }

    public ImageFrame? ReadImage()
    {
        desk.Note("clipboardReadImage");
        return Picture;
    }

    public void WriteImage(ImageFrame image)
    {
        desk.Note($"clipboardWriteImage {image.Width}x{image.Height}");
        Picture = image;
        Changes++;
    }

    public IReadOnlyList<string> ReadFiles()
    {
        desk.Note("clipboardReadFiles");
        return [.. Files];
    }

    public void WriteFiles(IReadOnlyList<string> paths)
    {
        desk.Note($"clipboardWriteFiles {string.Join("|", paths)}");
        Files.Clear();
        Files.AddRange(paths);
        Changes++;
    }

    public void Clear()
    {
        desk.Note("clipboardClear");
        Text = string.Empty;
        Changes++;
    }
}
