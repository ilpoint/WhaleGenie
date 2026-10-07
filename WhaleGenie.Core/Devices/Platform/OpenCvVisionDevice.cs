using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace WhaleGenie.Core.Devices.Platform;

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
        => FindAll(haystack, needle, confidencePercent, 1).FirstOrDefault();

    public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
        double confidencePercent, int limit)
    {
        var found = new List<ImageMatch>();
        if (haystack.IsEmpty || needle.IsEmpty || limit <= 0)
        {
            return found;
        }

        try
        {
            using var hay = Bgr(haystack);
            using var pin = Bgr(needle);
            if (pin.Width > hay.Width || pin.Height > hay.Height)
            {
                return found;
            }

            using var result = new Mat();
            Cv2.MatchTemplate(hay, pin, result, TemplateMatchModes.CCoeffNormed);

            // The whole map is scored once, then the best place is taken out of it and the next
            // best is read off the same map. A template the size of the reference cannot overlap
            // itself, so blanking the patch it covers is enough to move on to a different place.
            var size = new ScreenSize(pin.Width, pin.Height);
            while (found.Count < limit)
            {
                Cv2.MinMaxLoc(result, out _, out var best, out _, out var where);
                var score = Math.Clamp(best, -1, 1);
                if (score * 100 < confidencePercent)
                {
                    break;
                }

                found.Add(new ImageMatch(score, new ScreenPoint(where.X, where.Y), size));
                Suppress(result, where, pin.Width, pin.Height);
            }

            return found;
        }
        catch (Exception error) when (error is OpenCVException or DllNotFoundException)
        {
            throw new DeviceUnavailableException("image matching");
        }
    }

    /// <summary>Blanks out the patch one hit covers, so the next look lands somewhere else.</summary>
    private static void Suppress(Mat result, Point where, int width, int height)
    {
        var left = Math.Clamp(where.X, 0, result.Width - 1);
        var top = Math.Clamp(where.Y, 0, result.Height - 1);
        var right = Math.Clamp(where.X + width, left + 1, result.Width);
        var bottom = Math.Clamp(where.Y + height, top + 1, result.Height);

        using var patch = new Mat(result, new Rect(left, top, right - left, bottom - top));
        patch.SetTo(new Scalar(-1));
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
