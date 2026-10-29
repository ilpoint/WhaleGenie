using System.Collections.Generic;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend text recognition: what a picture reads as, and which pictures it was handed.
/// </summary>
internal sealed class FakeOcr(FakeDesk desk) : IOcrDevice
{
    /// <summary>What text recognition should report.</summary>
    public List<TextSpan> Spans { get; set; } = [];

    /// <summary>The sizes of the pictures the OCR was handed, so a test can see what was read.</summary>
    public List<(int Width, int Height)> OcrFrames { get; } = [];

    /// <summary>The pictures themselves, for a test that is about what was handed over to read.</summary>
    public List<ImageFrame> OcrPictures { get; } = [];

    public IReadOnlyList<TextSpan> Recognize(ImageFrame frame)
    {
        desk.Note("ocr");
        OcrFrames.Add((frame.Width, frame.Height));
        OcrPictures.Add(frame);
        return Spans;
    }
}
