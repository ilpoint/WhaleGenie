using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Finds things in a picture with a YOLO model the user brought along, run by OpenCV's own
/// inference. Nothing is shipped with the program: the model is a file a step names, and which
/// classes it knows is written in a label file beside it.
/// </summary>
/// <remarks>
/// Loading a model costs real time — a moment for a small one, seconds for a large one — so the
/// model a step asked for is kept until the program ends, together with the time its file was last
/// written so that one still being trained is read again. What is kept is bounded because a model
/// is large: the ones asked for most recently are held and the rest are let go.
/// </remarks>
public sealed class OpenCvDetectDevice : IDetectorDevice, IDisposable
{
    /// <summary>How many models are held at once. One macro usually brings one; two macros are two.</summary>
    private const int KeptModels = 3;

    /// <summary>How many label files are held at once. They are tiny, but a run is not endless.</summary>
    private const int KeptLabelFiles = 8;

    /// <summary>
    /// The one lock. OpenCV's own session is not safe to work from two threads at once, and two
    /// macros sharing this device would otherwise run the same model together.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>The models that are loaded, by the path of the file they were read from.</summary>
    private readonly Dictionary<string, Kept<Net>> _models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The names the label files hold, by the path of the file they were read from.</summary>
    private readonly Dictionary<string, Kept<IReadOnlyList<string>>> _labels =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Which file was asked for last, so that what is kept is what is being used.</summary>
    private long _used;

    public IReadOnlyList<DetectionHit> Detect(string modelPath, ImageFrame frame, DetectionQuery query)
    {
        var found = new List<DetectionHit>();
        var path = (modelPath ?? string.Empty).Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            throw new DeviceActionException(RunKeys.MissingModel, path);
        }

        if (frame.IsEmpty || query.Limit <= 0)
        {
            return found;
        }

        var names = Names(query.LabelFile);
        var wanted = YoloDetect.Wanted(query.Labels, names, out var unknown);
        if (unknown.Length > 0)
        {
            throw new DeviceActionException(RunKeys.UnknownLabel,
                names.Count == 0 ? unknown : $"{unknown} ({YoloDetect.Preview(names)})");
        }

        try
        {
            lock (_gate)
            {
                var net = Remember(_models, path, KeptModels, () => Model(path),
                    model => model.Dispose());

                var fit = YoloDetect.Fitted(frame.Width, frame.Height, query.InputSize);
                using var region = OpenCvVisionDevice.Bgr(frame);
                using var square = YoloDetect.Padded(region, fit);
                using var blob = Cv2.Dnn.BlobFromImage(square, 1.0 / 255,
                    new Size(fit.Input, fit.Input), new Scalar(), true, false);

                net.SetInput(blob);
                using var output = net.Forward();

                if (!YoloDetect.Recognised(output, out var channels, out var candidates))
                {
                    throw new DeviceActionException(RunKeys.BadModelShape, YoloDetect.Written(output));
                }

                foreach (var box in YoloDetect.Boxes(output, channels, candidates,
                    query.ConfidencePercent, query.IouPercent, wanted, query.Limit))
                {
                    var (corner, size) = YoloDetect.Placed(box, fit, frame.Width, frame.Height);
                    found.Add(new DetectionHit(box.Score, corner, size, box.ClassId,
                        box.ClassId >= 0 && box.ClassId < names.Count ? names[box.ClassId] : string.Empty));
                }
            }
        }
        catch (Exception error) when (error is OpenCVException)
        {
            // A file that is not a model at all, or one whose arithmetic OpenCV will not run: the
            // step is told which file, because the way out — another export, another input size —
            // starts from knowing what was read.
            throw new DeviceActionException(RunKeys.BadModel, $"{Path.GetFileName(path)}: {error.Message}");
        }
        catch (Exception error) when (error is DllNotFoundException or BadImageFormatException)
        {
            throw new DeviceUnavailableException("object detection");
        }

        return found;
    }

    /// <summary>
    /// Reads a model, refusing one that came back empty rather than carrying on with a session that
    /// will answer nothing.
    /// </summary>
    private static Net Model(string path)
    {
        var net = Cv2.Dnn.ReadNetFromOnnx(path, EngineType.Auto);
        if (net is null || net.Empty())
        {
            net?.Dispose();
            throw new DeviceActionException(RunKeys.BadModel, Path.GetFileName(path));
        }

        return net;
    }

    /// <summary>
    /// The class names a label file holds, in the order it lists them: the first line is class 0,
    /// which is the order a model answers its classes in. No file named, or a file that is not
    /// there, is a list of no names rather than a failure: the step may still name classes by
    /// number, and it is what a step that wants every class asks for.
    /// </summary>
    private IReadOnlyList<string> Names(string labelFile)
    {
        var path = (labelFile ?? string.Empty).Trim();
        if (path.Length == 0 || !File.Exists(path))
        {
            return [];
        }

        return Remember(_labels, path, KeptLabelFiles,
            () => YoloDetect.Names(TextEncoding.Read(File.ReadAllBytes(path), TextEncoding.Default)),
            _ => { });
    }

    /// <summary>
    /// What is kept for one file, read again when the file has been written since it was read.
    /// </summary>
    private T Remember<T>(Dictionary<string, Kept<T>> kept, string path, int most, Func<T> read,
        Action<T> letGo)
        where T : class
    {
        var stamp = Stamp(path);
        if (kept.TryGetValue(path, out var old))
        {
            if (stamp is not null && old.Stamp == stamp)
            {
                old.Used = ++_used;
                return old.Value;
            }

            letGo(old.Value);
            kept.Remove(path);
        }

        var value = read();
        kept[path] = new Kept<T>(stamp ?? 0, value) { Used = ++_used };

        while (kept.Count > most)
        {
            var oldest = kept.Aggregate((left, right) => left.Value.Used <= right.Value.Used
                ? left
                : right);
            letGo(oldest.Value.Value);
            kept.Remove(oldest.Key);
        }

        return value;
    }

    /// <summary>When a file was written, or null when it cannot be asked.</summary>
    private static long? Stamp(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var model in _models.Values)
            {
                model.Value.Dispose();
            }

            _models.Clear();
            _labels.Clear();
        }
    }

    /// <summary>One thing read from a file that is worth keeping, and the file it came from.</summary>
    private sealed class Kept<T>(long stamp, T value)
        where T : class
    {
        public long Stamp { get; } = stamp;

        public T Value { get; } = value;

        public long Used { get; set; }
    }
}

/// <summary>
/// The arithmetic of detection: fitting a region into the square a model reads, reading what the
/// model answered, and putting the boxes it found back where they were. It is kept apart from the
/// model and the machine so that both halves can be checked without either.
/// </summary>
internal static class YoloDetect
{
    /// <summary>How many of a label file's names a failure message spells out before trailing off.</summary>
    private const int NamesInMessage = 10;

    /// <summary>
    /// The grey a padded edge is filled with. It is the number YOLO's own converters pad with, and
    /// the model was trained on bands of it, so a band of it is "nothing here" rather than something
    /// to be found the way a black band might be.
    /// </summary>
    private const byte Grey = 114;

    /// <summary>
    /// How one region was fitted into the square: the size of the square, how much the region was
    /// scaled to fit it, and the band of grey left on each side so that the region keeps its shape
    /// instead of being stretched. The band is halved, one half each side, so the region sits in the
    /// middle of the square — which is where a box is put back from, so the two have to agree.
    /// </summary>
    internal readonly record struct Fit(int Input, double Scale, int PadX, int PadY);

    /// <summary>How a region is fitted into the model's square, keeping its shape.</summary>
    internal static Fit Fitted(int width, int height, int input)
    {
        var size = Math.Max(32, input);
        var scale = Math.Min(size / (double)Math.Max(1, width), size / (double)Math.Max(1, height));
        var across = Math.Min(size, Math.Max(1, (int)Math.Round(width * scale)));
        var down = Math.Min(size, Math.Max(1, (int)Math.Round(height * scale)));
        return new Fit(size, scale, (size - across) / 2, (size - down) / 2);
    }

    /// <summary>
    /// The region scaled into the middle of the model's square, with what is left over filled with
    /// grey. The grey is the number YOLO's own converters pad with, split the same way they split it.
    /// </summary>
    internal static Mat Padded(Mat region, Fit fit)
    {
        var square = new Mat(fit.Input, fit.Input, MatType.CV_8UC3, new Scalar(Grey, Grey, Grey));
        var across = Math.Min(fit.Input, Math.Max(1, (int)Math.Round(region.Width * fit.Scale)));
        var down = Math.Min(fit.Input, Math.Max(1, (int)Math.Round(region.Height * fit.Scale)));

        // The half band is taken off again when a box is put back, so it has to be left on both sides
        // rather than all of it on the right and the bottom.
        using var middle = new Mat(square, new Rect(fit.PadX, fit.PadY, across, down));
        Cv2.Resize(region, middle, middle.Size(), 0, 0, InterpolationFlags.Linear);
        return square;
    }

    /// <summary>
    /// Whether this output is the shape this step knows — one picture, a channel for each side of a
    /// box followed by a score for each class, and a column for each guess — and how big it is.
    /// </summary>
    /// <remarks>
    /// The four sides come first and a score per class follows, so four channels or fewer name no
    /// class at all. The shorter of the two middle sides is the one holding the channels: a model
    /// exported the other way round — an older YOLO, which also carries a score for "something is
    /// here" — keeps its guesses on the shorter side, and is turned away rather than misread.
    /// </remarks>
    internal static bool Recognised(Mat output, out int channels, out int candidates)
    {
        channels = 0;
        candidates = 0;
        if (output.Dims != 3 || output.Size(0) != 1 || output.Type() != MatType.CV_32F
            || !output.IsContinuous())
        {
            return false;
        }

        channels = output.Size(1);
        candidates = output.Size(2);
        return channels > 4 && channels <= candidates;
    }

    /// <summary>The shape of an output, written the way a person reads it: 1x84x8400.</summary>
    internal static string Written(Mat output)
        => string.Join('x', Enumerable.Range(0, Math.Max(1, output.Dims))
            .Select(side => output.Size(side)));

    /// <summary>
    /// What the output holds: every guess sure enough to count, as a box in the model's own square.
    /// A guess is named by the class that scored highest for it, and the classes the step asked for
    /// are the only ones collected. Guesses lying over each other and naming the same class are kept
    /// once, the surest of them, because a model answers with several guesses about one thing.
    /// </summary>
    internal static List<Box> Boxes(Mat output, int channels, int candidates,
        double confidencePercent, double iouPercent, IReadOnlyList<int>? classes, int limit)
    {
        var guesses = new List<Box>();
        var wanted = classes is { Count: > 0 } ? new HashSet<int>(classes) : null;
        if ((long)channels * candidates > int.MaxValue)
        {
            return guesses;
        }

        // The whole answer is copied out at once: reading it one number at a time through the
        // managed wrapper costs more than the model's own arithmetic does.
        var values = new float[channels * candidates];
        Marshal.Copy(output.Data, values, 0, values.Length);

        for (var column = 0; column < candidates; column++)
        {
            var best = -1;
            var score = float.NegativeInfinity;
            for (var channel = 4; channel < channels; channel++)
            {
                var value = values[((long)channel * candidates) + column];
                if (value > score)
                {
                    score = value;
                    best = channel - 4;
                }
            }

            if (score * 100 < confidencePercent || (wanted is not null && !wanted.Contains(best)))
            {
                continue;
            }

            guesses.Add(new Box(
                values[column],
                values[candidates + column],
                values[(2 * candidates) + column],
                values[(3 * candidates) + column],
                score,
                best));
        }

        if (guesses.Count == 0)
        {
            return guesses;
        }

        // The guesses as rectangles in the square the model was fed, which is the space OpenCV's own
        // overlap test works in; what comes back is which of them to keep.
        var kept = Array.Empty<int>();
        Cv2.Dnn.NMSBoxesBatched(
            guesses.Select(Rect).ToArray(),
            guesses.Select(box => box.Score).ToArray(),
            guesses.Select(box => box.ClassId).ToArray(),
            (float)(confidencePercent / 100),
            (float)(iouPercent / 100),
            out kept);

        return
        [
            .. kept
                .Where(index => index >= 0 && index < guesses.Count)
                .Distinct()
                .Select(index => guesses[index])
                .OrderByDescending(box => box.Score)
                .Take(Math.Max(1, limit))
        ];
    }

    /// <summary>Where a box sits in the region it was found in, and how big it is there.</summary>
    internal static (ScreenPoint Corner, ScreenSize Size) Placed(Box box, Fit fit,
        int width, int height)
    {
        var left = (int)Math.Round((box.CentreX - (box.Width / 2) - fit.PadX) / fit.Scale);
        var top = (int)Math.Round((box.CentreY - (box.Height / 2) - fit.PadY) / fit.Scale);
        var right = (int)Math.Round((box.CentreX + (box.Width / 2) - fit.PadX) / fit.Scale);
        var bottom = (int)Math.Round((box.CentreY + (box.Height / 2) - fit.PadY) / fit.Scale);

        // A model may guess a little outside the region — the grey band and the edge the region was
        // scaled to both invite it — so what is handed over is the part of the box really inside the
        // region, which is also what a region handed to a later step has to hold.
        var fromLeft = Math.Clamp(left, 0, Math.Max(0, width - 1));
        var fromTop = Math.Clamp(top, 0, Math.Max(0, height - 1));
        var toRight = Math.Clamp(right, fromLeft + 1, Math.Max(fromLeft + 1, width));
        var toBottom = Math.Clamp(bottom, fromTop + 1, Math.Max(fromTop + 1, height));

        return (new ScreenPoint(fromLeft, fromTop),
            new ScreenSize(toRight - fromLeft, toBottom - fromTop));
    }

    /// <summary>
    /// The class names a label file holds, in the order it lists them: the first line is class 0,
    /// which is the order a model answers its classes in. Blank lines name nothing and take no
    /// number of their own, so a file that was spaced out for reading still lines up with the model.
    /// </summary>
    internal static IReadOnlyList<string> Names(string text)
        =>
        [
            .. text
                .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
        ];

    /// <summary>
    /// The classes a step asked for, as numbers: a row reading as a number is that class whatever
    /// the label file says, and anything else is a name the label file has to hold, held up against
    /// its own names ignoring case and the spaces around them. No rows at all means every class,
    /// which is what a step that named none asks for. The name that could not be placed comes back
    /// in <paramref name="unknown"/>, which is empty when every row was placed.
    /// </summary>
    internal static IReadOnlyList<int>? Wanted(IReadOnlyList<string>? labels,
        IReadOnlyList<string> names, out string unknown)
    {
        unknown = string.Empty;
        var rows = labels?.Where(row => !string.IsNullOrWhiteSpace(row)).Select(row => row.Trim())
            .ToList() ?? [];
        if (rows.Count == 0)
        {
            return null;
        }

        var wanted = new List<int>();
        foreach (var row in rows)
        {
            if (int.TryParse(row, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                wanted.Add(number);
                continue;
            }

            var found = -1;
            for (var index = 0; index < names.Count; index++)
            {
                if (string.Equals(names[index].Trim(), row, StringComparison.OrdinalIgnoreCase))
                {
                    found = index;
                    break;
                }
            }

            if (found < 0)
            {
                unknown = row;
                return null;
            }

            wanted.Add(found);
        }

        return wanted;
    }

    /// <summary>The first few names of a label file, for a failure that has to say what is in it.</summary>
    internal static string Preview(IReadOnlyList<string> names)
        => string.Join(", ", names.Take(NamesInMessage))
            + (names.Count > NamesInMessage ? ", …" : string.Empty);

    /// <summary>One guess as the rectangle OpenCV's overlap test takes: a corner and a size.</summary>
    private static Rect Rect(Box box) => new(
        (int)Math.Round(box.CentreX - (box.Width / 2)),
        (int)Math.Round(box.CentreY - (box.Height / 2)),
        Math.Max(1, (int)Math.Round(box.Width)),
        Math.Max(1, (int)Math.Round(box.Height)));
}

/// <summary>
/// One guess a model made, in the square the model was fed: the middle of it, its size, how sure the
/// model was and the number of the class it says it is.
/// </summary>
internal sealed record Box(
    float CentreX,
    float CentreY,
    float Width,
    float Height,
    float Score,
    int ClassId);
