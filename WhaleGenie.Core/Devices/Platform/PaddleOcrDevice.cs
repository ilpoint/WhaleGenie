using System;
using System.Collections.Generic;
using System.Linq;
using Sdcb.SimdPaddleOCR;
using Sdcb.SimdPaddleOCR.Models.ChineseV6Tiny;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Reads text off the screen with PP-OCR. The models are embedded in their own package, so
/// nothing is downloaded and nothing is written to disk. The engine hands over pixels in the
/// layout this recognises directly, without a copy.
/// </summary>
public sealed class PaddleOcrDevice : IOcrDevice, IDisposable
{
    /// <summary>Loading the models costs real time, so it waits until a macro reads text.</summary>
    private readonly Lazy<PaddleOcrAll> _ocr;

    public PaddleOcrDevice()
        : this(() => PaddleOcrAll.Load(ChineseV6TinyModels.Default, new PaddleOcrOptions()))
    {
    }

    /// <summary>
    /// The same reader with the loading of the models handed in, which is what a check does so that
    /// it can watch the loading happen without reading a real screen or loading a real model.
    /// </summary>
    internal PaddleOcrDevice(Func<PaddleOcrAll> load) => _ocr = new Lazy<PaddleOcrAll>(load);

    /// <summary>
    /// Loads the models now rather than at the first step that reads text. Loading costs a moment,
    /// and the step that pays for it is usually the first text step of a run; this is what the
    /// 「预加载文字识别」 setting turns on, so a run that reads text starts at its own pace. A machine
    /// that cannot load them says so when a step needs the reading rather than here.
    /// </summary>
    public void WarmUp()
    {
        try
        {
            _ = _ocr.Value;
        }
        catch (Exception)
        {
            // Nothing is waiting on this: a warm-up that fails is not a failure of anything the user
            // asked for. A load that failed is remembered the same way a load that worked is, so
            // this is not tried again.
        }
    }

    public IReadOnlyList<TextSpan> Recognize(ImageFrame frame)
    {
        if (frame.IsEmpty)
        {
            return [];
        }

        PaddleOcrResult result;
        try
        {
            result = _ocr.Value.Run(frame.Bgra, frame.Width, frame.Height, 0, ImagePixelFormat.Bgra32);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new DeviceUnavailableException($"text recognition ({error.Message})");
        }

        return
        [
            .. result.Lines
                .Where(line => !string.IsNullOrWhiteSpace(line.Text))
                .Select(line => new TextSpan(
                    line.Text.Trim(),
                    new ScreenPoint(
                        (int)Math.Round(Left(line)),
                        (int)Math.Round(Top(line))),
                    new ScreenSize(
                        Math.Max(1, (int)Math.Round(line.Box.Width)),
                        Math.Max(1, (int)Math.Round(line.Box.Height))),
                    // The model's own line score. PaddleOCR proper averages the softmax
                    // probability of each character; this port averages the winning logit
                    // instead, so the number runs from roughly ten for rubbish to roughly forty
                    // for clean writing instead of sitting between zero and one. It still ranks
                    // readings against each other, which is all a macro needs it for.
                    line.RecognitionScore))
        ];
    }

    public void Dispose()
    {
        if (_ocr.IsValueCreated)
        {
            _ocr.Value.Dispose();
        }
    }

    private static float Left(PaddleOcrLine line)
        => Math.Min(Math.Min(line.Box.X1, line.Box.X2), Math.Min(line.Box.X3, line.Box.X4));

    private static float Top(PaddleOcrLine line)
        => Math.Min(Math.Min(line.Box.Y1, line.Box.Y2), Math.Min(line.Box.Y3, line.Box.Y4));
}
