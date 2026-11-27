using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Taking a picture of the screen: a rectangle a step names, a whole window, and waiting for what
/// is on it to stop moving. The picture is kept under the name the step gave it and written out
/// when the step asked for a file, so one step takes and keeps rather than two.
/// </summary>
internal static class CaptureActions
{
    /// <summary>
    /// The capture actions, each answered by the method written for it. A step of another family
    /// is not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["vision.capture"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => Capture(run, step, depth));
                return Signal.Normal;
            },

            ["vision.captureWindow"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => CaptureWindow(run, step, depth));
                return Signal.Normal;
            },

            ["vision.waitStable"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await WaitStable(run, step, depth, token);
                return Signal.Normal;
            },
        };

    /// <summary>Copies a region of the screen into a variable the macro can look at again.</summary>
    private static void Capture(IRunContext run, ExecutableStep step, int depth)
    {
        var (frame, corner) = CaptureBox(run, step);
        Keep(run, step, depth, frame, corner);
    }

    /// <summary>
    /// One look for the trial window: what the step would take right now, without keeping it or
    /// writing anything. Waiting for the screen to hold still is not something one look can answer,
    /// so that step shows what is on the screen and leaves the waiting to a run.
    /// </summary>
    internal static void Preview(IRunContext run, ExecutableStep step)
    {
        switch (step.Type)
        {
            case "vision.capture":
                run.Reads.Showing(() => ShowCapture(run, step, CaptureBox(run, step)));
                break;

            case "vision.captureWindow":
                run.Reads.Showing(() =>
                {
                    var (frame, corner, _) = WindowBox(run, step);
                    ShowCapture(run, step, (frame, corner));
                });
                break;

            default:
                run.Reads.Showing(() =>
                {
                    if (run.Reads.Areas(step).FirstOrDefault() is
                        { Frame: { } frame, Origin: { } origin })
                    {
                        ShowCapture(run, step, (frame, origin));
                    }
                });
                break;
        }
    }

    /// <summary>
    /// Shows a picture a step would have taken, with the rectangle it covers marked. Nothing is
    /// handed over: a trial look writes no result, so the name is only what the run would use.
    /// </summary>
    private static void ShowCapture(IRunContext run, ExecutableStep step,
        (ImageFrame Frame, ScreenPoint Corner) taken)
        => run.Shots.Show(step, LookKind.Capture, taken.Frame, taken.Corner,
            [
                new LookBox(new ImageMatch(0, taken.Corner,
                    new ScreenSize(taken.Frame.Width, taken.Frame.Height)), LookRole.Area),
            ],
            1, step.Return("image"));

    /// <summary>
    /// The rectangle a capture step covers, taken off the screen. The corner is worked out first so
    /// that the rectangle reported is the one on screen, which is what a later "region" naming this
    /// picture has to read.
    /// </summary>
    internal static (ImageFrame Frame, ScreenPoint Corner) CaptureBox(IRunContext run,
        ExecutableStep step)
    {
        var anchor = run.Points.AnchorOf(step);
        var (corner, width, height) = Box(run, step, anchor.Corner, anchor.Window);
        var shot = run.Shots.Take(corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Frame, shot.Origin);
    }

    /// <summary>
    /// The rectangle a step names, whether it reads or copies one part of the screen: the record it
    /// takes the rectangle from, or the four numbers it writes down. Every record that carries a
    /// rectangle carries all four sides of it, so a hit, a window or a picture taken earlier goes in
    /// as it stands — "read the writing on what was just found" is one field rather than four
    /// readings of <c>field()</c> with a chance of one of them being the neighbour's. A record's
    /// numbers are screen pixels, the way every record's are; numbers written out are counted from
    /// <paramref name="origin"/>, which is the corner the step says its own numbers are measured
    /// from. A side nobody wrote comes to whatever the catalogue says its empty cell means: a
    /// reading step runs to the edge of the screen.
    /// </summary>
    private static (ScreenPoint Corner, int Width, int Height) Box(IRunContext run,
        ExecutableStep step, ScreenPoint? origin, WindowInfo? window)
    {
        if (run.RectOf(step, "rect") is { } sides)
        {
            return (new ScreenPoint(sides.Left, sides.Top), sides.Width, sides.Height);
        }

        var args = run.Args(step);
        var corner = CapturePlan.Placed(origin, args.Number("x"), args.Number("y"));
        int right;
        int bottom;
        if (window is { } target)
        {
            var originOfWindow = run.Devices.Windows.ClientOrigin(target.Handle);
            var size = run.Devices.Windows.ClientSize(target.Handle);
            right = originOfWindow.X + size.Width;
            bottom = originOfWindow.Y + size.Height;
        }
        else
        {
            var desk = run.Devices.Screen.Desktop.Bounds;
            right = desk.Right;
            bottom = desk.Bottom;
        }

        return (corner,
            CapturePlan.Reach(args.Given(FieldNames.Width), args.EmptyMeans(FieldNames.Width),
                args.Number(FieldNames.Width), corner.X, right),
            CapturePlan.Reach(args.Given(FieldNames.Height), args.EmptyMeans(FieldNames.Height),
                args.Number(FieldNames.Height), corner.Y, bottom));
    }

    /// <summary>
    /// Copies a whole window into a variable. The window's own rectangle is what is copied, so the
    /// title bar and the border come with it, and a window that is not in front can be read too.
    /// </summary>
    private static void CaptureWindow(IRunContext run, ExecutableStep step, int depth)
    {
        var (frame, corner, window) = WindowBox(run, step);
        Keep(run, step, depth, frame, corner, window);
    }

    /// <summary>The whole of the window a step names, taken off the screen.</summary>
    internal static (ImageFrame Frame, ScreenPoint Corner, WindowInfo Window) WindowBox(
        IRunContext run, ExecutableStep step)
    {
        var window = run.Locate(step);
        var shot = run.Shots.Take(window.Location.X, window.Location.Y, window.Size.Width,
            window.Size.Height, window);
        return (shot.Frame, shot.Origin, window);
    }

    /// <summary>
    /// Keeps a picture under the name a step gave it, with the rectangle it covers beside it, so a
    /// later step can search inside it or hand the region to something else. The size written down
    /// is the picture's own rather than the size that was asked for: a capture that ran off the
    /// edge of the screen comes back smaller, and a search region that is bigger than the picture
    /// would look at pixels that are not there.
    /// </summary>
    private static void Keep(IRunContext run, ExecutableStep step, int depth, ImageFrame frame,
        ScreenPoint corner, WindowInfo? sourceWindow = null)
    {
        // The picture is kept under the name of the image result, and only there: a step that was
        // told not to hand the picture over leaves nothing for a later step to search in, which is
        // what leaving that result unticked means.
        var name = run.Give(step, "image", Value.FromImage($"<image {frame.Width}x{frame.Height}>"));
        if (name.Length > 0)
        {
            run.Images[name] = frame;
        }

        run.Shots.GiveTaken(step);

        // Where the picture sits is one record beside the picture, so it can be handed to a search
        // region as it stands or read one part at a time. The middle is in it beside the corner:
        // that is the point a step aims at, and a macro that means the middle of what it just took
        // should not have to work it out of two halves of the rectangle.
        var middle = ScreenMath.Middle(corner, new ScreenSize(frame.Width, frame.Height));
        ScreenPoint? client = null;
        if (sourceWindow is { } source)
        {
            var origin = run.Devices.Windows.ClientOrigin(source.Handle);
            client = new ScreenPoint(middle.X - origin.X, middle.Y - origin.Y);
        }

        run.Points.GivePoints(step, middle, client);
        run.Give(step, "place", Value.FromRecord(
        [
            (FieldNames.X, Value.FromNumber(middle.X)),
            (FieldNames.Y, Value.FromNumber(middle.Y)),
            (FieldNames.Left, Value.FromNumber(corner.X)),
            (FieldNames.Top, Value.FromNumber(corner.Y)),
            (FieldNames.Width, Value.FromNumber(frame.Width)),
            (FieldNames.Height, Value.FromNumber(frame.Height)),
            (FieldNames.Box, Value.FromText($"{corner.X},{corner.Y},{frame.Width},{frame.Height}")),
        ]));

        run.Shots.Show(step, LookKind.Capture, frame, corner,
            [new LookBox(new ImageMatch(0, corner, new ScreenSize(frame.Width, frame.Height)),
                LookRole.Area)],
            1, name);
        if (name.Length > 0)
        {
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.Capture, name,
                $"{frame.Width}x{frame.Height}");
        }

        KeepFile(run, step, depth, frame);
        run.Give(step, "path", Value.FromText(SavedPath(run, step)));
    }

    /// <summary>
    /// Writes the picture out where the step asked for a file, when it asked for one: one step takes
    /// the picture and keeps it, rather than a capture followed by a step that writes what it left
    /// behind. It is always a PNG, which is the small end of what a picture can be written as, and a
    /// file of that name is replaced — the same answer every other step that writes a file gives.
    /// </summary>
    private static void KeepFile(IRunContext run, ExecutableStep step, int depth, ImageFrame frame)
    {
        var path = run.PathOf(step);
        if (path.Trim().Length == 0)
        {
            return;
        }

        run.Devices.Files.WriteBytes(path, PngWriter.Encode(frame));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.SavedPicture, SavedPath(run, step),
            $"{frame.Width}x{frame.Height}");
    }

    /// <summary>
    /// The file a capture wrote, in the form a later step can open: the macros folder is where a
    /// relative path is measured from, and the result should say the same place that was written.
    /// </summary>
    private static string SavedPath(IRunContext run, ExecutableStep step)
    {
        var path = run.PathOf(step);
        return path.Trim().Length == 0 ? string.Empty : run.Devices.Files.Resolve(path);
    }

    /// <summary>
    /// Waits for the picture to hold still: the screen counts as settled once every watched area
    /// has stayed within the tolerance of the frame that started the wait for a whole quiet
    /// stretch. A fixed pause is what this replaces, and it is wrong in both directions — too
    /// short and the step after it works on half a picture, too long and every run pays for the
    /// slowest case.
    /// </summary>
    private static async Task WaitStable(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var args = run.Args(step);
        var tolerance = args.ValueOf("tolerance").AsNumber();
        var allowed = args.ValueOf("changedPercent").AsNumber() / 100;
        var quiet = Math.Max(0, args.Number("quietMs"));

        var started = Stopwatch.GetTimestamp();
        long Elapsed() => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        var anchor = run.Reads.Areas(step);
        var since = Elapsed();

        while (true)
        {
            await run.Reads.WaitForScreen(token);
            var now = run.Reads.Areas(step);
            if (Still(anchor, now, tolerance, allowed))
            {
                if (Elapsed() - since >= quiet)
                {
                    if (now.FirstOrDefault() is { Frame: { } frame, Origin: { } origin })
                    {
                        run.Points.GivePoints(step,
                            ScreenMath.Middle(origin, new ScreenSize(frame.Width, frame.Height)),
                            null);
                    }

                    run.Log(LogLevel.Info, depth, step.Type, LogKeys.FrameStill, Elapsed());
                    return;
                }
            }
            else
            {
                // Counted from this frame on: what has to hold still for the quiet stretch is not
                // the picture the wait opened on, which may have been another screen altogether,
                // but the picture the screen has just arrived at.
                anchor = now;
                since = Elapsed();
            }
        }
    }

    /// <summary>
    /// True when every watched area is the picture it is compared against, as far as the step is
    /// willing to say so: no more of it than the step allows may have moved by more than its
    /// colour tolerance.
    /// </summary>
    private static bool Still(IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> before,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> after, double tolerance, double allowed)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        for (var index = 0; index < before.Count; index++)
        {
            if (ImageFrame.ChangedShare(before[index].Frame, after[index].Frame, tolerance) > allowed)
            {
                return false;
            }
        }

        return true;
    }
}
