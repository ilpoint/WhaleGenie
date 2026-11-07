using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>Looks for a reference picture on screen, either pixel for pixel or by its features.</summary>
public sealed class OpenCvVisionDevice : IVisionDevice
{
    /// <summary>
    /// How much better one pair of features has to be than the next best pair for the same feature
    /// before it is believed. Below this a feature looks like it matched by luck.
    /// </summary>
    private const double FeatureRatio = 0.75;

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

    public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle, VisionQuery query)
    {
        var found = new List<ImageMatch>();
        if (haystack.IsEmpty || needle.IsEmpty || query.Limit <= 0)
        {
            return found;
        }

        try
        {
            return query.Algorithm is MatchAlgorithm.Feature
                ? ByFeature(haystack, needle, query)
                : ByTemplate(haystack, needle, query);
        }
        catch (Exception error) when (error is OpenCVException or DllNotFoundException)
        {
            throw new DeviceUnavailableException("image matching");
        }
    }

    /// <summary>
    /// Looks for the reference picture by comparing the two pictures pixel for pixel, and reports
    /// every place it was found. The whole map of scores is worked out once, and then the best
    /// place is taken out of it and the next best is read off the same map: a reference picture the
    /// size of the area cannot overlap itself, so blanking the patch a hit covers is enough to move
    /// on to a different place.
    /// </summary>
    private static List<ImageMatch> ByTemplate(ImageFrame haystack, ImageFrame needle,
        VisionQuery query)
    {
        var found = new List<ImageMatch>();
        using (var hay = Bgr(haystack))
        using (var pin = Bgr(needle))
        {
            if (pin.Width > hay.Width || pin.Height > hay.Height)
            {
                return found;
            }

            using var result = new Mat();
            var exact = query.Algorithm is MatchAlgorithm.Difference;
            if (query.Transparent is { } transparent)
            {
                using var mask = Mask(needle, transparent, query.TolerancePercent);
                Cv2.MatchTemplate(hay, pin, result, Mode(query, masked: true), mask);
            }
            else
            {
                Cv2.MatchTemplate(hay, pin, result, Mode(query, masked: false));
            }

            var size = new ScreenSize(pin.Width, pin.Height);
            while (found.Count < query.Limit)
            {
                Cv2.MinMaxLoc(result, out var low, out var high, out var lowAt, out var highAt);

                // A square difference is the distance between the pictures rather than how alike
                // they are: the smallest one is the best, and it is read out the other way round so
                // that a macro's confidence means "bigger is surer" whichever way was chosen.
                var where = exact ? lowAt : highAt;
                var score = exact ? 1 - low : Math.Clamp(high, -1, 1);
                if (score * 100 < query.ConfidencePercent)
                {
                    break;
                }

                found.Add(new ImageMatch(score, new ScreenPoint(where.X, where.Y), size));
                Suppress(result, where, pin.Width, pin.Height);
            }

            return found;
        }
    }

    /// <summary>
    /// Looks for the reference picture by its features: what stands out in it is found in both
    /// pictures and paired up. That is slower than comparing pixel for pixel, and it finds the
    /// thing when it is drawn at another size or has something small changed about it — which is
    /// what a macro wants when the game's own scale is not the one the reference was taken at.
    /// </summary>
    private static List<ImageMatch> ByFeature(ImageFrame haystack, ImageFrame needle,
        VisionQuery query)
    {
        var found = new List<ImageMatch>();
        using var hay = Gray(haystack);
        using var pin = Gray(needle);

        using var finder = SIFT.Create();
        using var wantedParts = new Mat();
        using var foundParts = new Mat();

        // The part of the reference picture a step called transparent is left out here too: a
        // number that keeps changing would otherwise be paired up on the strength of whatever it
        // happens to say this time, which is how a search finds things that are not there. An empty
        // mask is how every part of the picture is taken into account.
        using var mask = query.Transparent is { } leaveOut
            ? Mask(needle, leaveOut, query.TolerancePercent)
            : new Mat();
        using var everywhere = new Mat();
        finder.DetectAndCompute(pin, mask, out var wanted, wantedParts);
        finder.DetectAndCompute(hay, everywhere, out var spots, foundParts);

        if (wanted.Length < query.MinFeatures || spots.Length < query.MinFeatures
            || wantedParts.Empty() || foundParts.Empty())
        {
            return found;
        }

        using var matcher = new BFMatcher(NormTypes.L2);
        var pairs = matcher.KnnMatch(wantedParts, foundParts, 2);

        // A pair is only believed when it is clearly better than the next best one for the same
        // feature. Without that test most of the pairs are only together because there was nothing
        // better for them, which is how a search finds things that are not there.
        var good = new List<DMatch>();
        foreach (var pair in pairs)
        {
            if (pair.Length == 2 && pair[0].Distance < FeatureRatio * pair[1].Distance)
            {
                good.Add(pair[0]);
            }
        }

        if (good.Count < query.MinFeatures)
        {
            return found;
        }

        // Where the whole of it landed, taken as the middle of what each pair says: the reference
        // feature sits at one place in the reference picture and another in the area, and the
        // difference between the two is where the picture has to be for that pair to be right.
        var across = new double[good.Count];
        var down = new double[good.Count];
        for (var index = 0; index < good.Count; index++)
        {
            across[index] = spots[good[index].TrainIdx].Pt.X - wanted[good[index].QueryIdx].Pt.X;
            down[index] = spots[good[index].TrainIdx].Pt.Y - wanted[good[index].QueryIdx].Pt.Y;
        }

        Array.Sort(across);
        Array.Sort(down);
        var left = Math.Clamp((int)Math.Round(Middle(across)), 0,
            Math.Max(0, haystack.Width - needle.Width));
        var top = Math.Clamp((int)Math.Round(Middle(down)), 0,
            Math.Max(0, haystack.Height - needle.Height));

        // How much of the reference picture's own detail was found again, as a fraction: a
        // measurement of how much of it was recognised rather than a percentage of certainty. It
        // is on the same scale as a pixel comparison's score, so one confidence number means the
        // same thing whichever way the picture is looked for.
        var score = (double)good.Count / wanted.Length;
        if (score * 100 < query.ConfidencePercent)
        {
            return found;
        }

        found.Add(new ImageMatch(score, new ScreenPoint(left, top),
            new ScreenSize(needle.Width, needle.Height)));
        return found;
    }

    /// <summary>The middle value of a sorted list, which one wild pair cannot move.</summary>
    private static double Middle(double[] sorted) => sorted[sorted.Length / 2];

    /// <summary>The OpenCV way of comparing two pictures that the step asked for.</summary>
    /// <remarks>
    /// 带掩码时，默认的归一化相关不能照用：OpenCV 在掩码下对这一档给的是坏结果。同一个图标、
    /// 同一个掩码，逐像素与相关都答 1.000 并在图标原处，归一化相关答 0.196、位置还是负数。
    /// 掩码下改走相关，两者都只数掩码之内的像素，分数仍是「越像越高」。
    /// </remarks>
    private static TemplateMatchModes Mode(VisionQuery query, bool masked) => query.Algorithm switch
    {
        MatchAlgorithm.Correlated => TemplateMatchModes.CCorrNormed,
        MatchAlgorithm.Difference => TemplateMatchModes.SqDiffNormed,
        MatchAlgorithm.Feature => TemplateMatchModes.CCoeffNormed,
        _ => masked ? TemplateMatchModes.CCorrNormed : TemplateMatchModes.CCoeffNormed,
    };

    /// <summary>
    /// Which pixels of the reference picture take part in the comparing: everything except the
    /// colour the step called transparent, which is how a step looks for an icon drawn over a
    /// background it does not control — a flat colour behind it, a number over a button, a bar that
    /// fills up.
    /// </summary>
    /// <remarks>
    /// The walk over the picture is done here rather than by an InRange call because the tolerance
    /// a step writes has to mean the same thing as it does in the colour family, and that rule
    /// lives in <see cref="ColourReach"/> alone. A reference picture is small, so walking it costs
    /// nothing next to matching it.
    /// </remarks>
    private static Mat Mask(ImageFrame needle, PixelColor transparent, double tolerancePercent)
    {
        var reach = new ColourReach(transparent, tolerancePercent);
        var pixels = needle.Bgra.AsSpan();
        var weight = new byte[needle.Width * needle.Height];

        for (var at = 0; at < pixels.Length; at += 4)
        {
            weight[at / 4] = reach.Near(pixels, at) ? (byte)0 : (byte)255;
        }

        var mask = new Mat(needle.Height, needle.Width, MatType.CV_8UC1);
        Marshal.Copy(weight, 0, mask.Data, weight.Length);
        return mask;
    }

    /// <summary>Copies a frame into the one channel layout features are found in.</summary>
    private static Mat Gray(ImageFrame frame)
    {
        using var four = new Mat(frame.Height, frame.Width, MatType.CV_8UC4);
        Marshal.Copy(frame.Bgra, 0, four.Data, frame.Bgra.Length);

        var gray = new Mat();
        Cv2.CvtColor(four, gray, ColorConversionCodes.BGRA2GRAY);
        return gray;
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

    /// <summary>
    /// Copies a frame into the three channel layout OpenCV works in. It is shared with the detection
    /// device rather than written twice: the layout a picture has to be in is one answer for both.
    /// </summary>
    internal static Mat Bgr(ImageFrame frame)
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
