using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Viktor.Core.Devices.Platform;

/// <summary>Looks for a reference picture on screen by template matching.</summary>
public sealed class OpenCvVisionDevice : IVisionDevice
{
    public ImageFrame? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var source = Cv2.ImRead(path, ImreadModes.Color);
            if (source.Empty())
            {
                return null;
            }

            using var converted = new Mat();
            Cv2.CvtColor(source, converted, ColorConversionCodes.BGR2BGRA);
            return Frame(converted);
        }
        catch (Exception error) when (error is OpenCVException or DllNotFoundException)
        {
            throw new DeviceUnavailableException("image matching");
        }
    }

    public ImageMatch? Find(ImageFrame haystack, ImageFrame needle, double confidencePercent)
    {
        if (haystack.IsEmpty || needle.IsEmpty)
        {
            return null;
        }

        try
        {
            using var hay = Bgr(haystack);
            using var pin = Bgr(needle);
            if (pin.Width > hay.Width || pin.Height > hay.Height)
            {
                return null;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(hay, pin, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out var best, out _, out var where);

            var score = Math.Clamp(best, -1, 1);
            return score * 100 >= confidencePercent
                ? new ImageMatch(score, new ScreenPoint(where.X, where.Y), new ScreenSize(pin.Width, pin.Height))
                : null;
        }
        catch (Exception error) when (error is OpenCVException or DllNotFoundException)
        {
            throw new DeviceUnavailableException("image matching");
        }
    }

    /// <summary>Copies a frame into the three channel layout OpenCV compares with.</summary>
    private static Mat Bgr(ImageFrame frame)
    {
        using var four = new Mat(frame.Height, frame.Width, MatType.CV_8UC4);
        Marshal.Copy(frame.Bgra, 0, four.Data, frame.Bgra.Length);

        var three = new Mat();
        Cv2.CvtColor(four, three, ColorConversionCodes.BGRA2BGR);
        return three;
    }

    /// <summary>Copies a Mat back into the layout the rest of the engine uses.</summary>
    private static ImageFrame Frame(Mat source)
    {
        var length = (int)(source.Total() * source.ElemSize());
        var bytes = new byte[length];
        Marshal.Copy(source.Data, bytes, 0, length);
        return new ImageFrame(source.Width, source.Height, bytes);
    }
}
