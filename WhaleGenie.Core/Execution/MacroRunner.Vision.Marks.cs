using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Pointing at what was found: the boxes a mark is drawn from, and a single pixel read.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Adds where a step has just acted to the look it took to get there, so a step that clicked
    /// the wrong place can be seen rather than guessed at.
    /// </summary>
    private void Aim(ScreenPoint point)
    {
        if (_looks is null || _saw is null)
        {
            return;
        }

        var boxes = new List<LookBox>(_saw.Boxes)
        {
            new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
        };

        _saw = _saw with { Boxes = boxes, ChosenIndex = boxes.Count };
    }

    /// <summary>
    /// A look at a single pixel: a square of screen around it with the pixel itself marked, which
    /// is what a step watching for a colour has to show for itself.
    /// </summary>
    private void SawPixel(ExecutableStep step, ScreenPoint point, string looking,
        WindowInfo? window)
    {
        if (_looks is null)
        {
            return;
        }

        var desk = _devices.Screen.Desktop.Bounds;
        var left = Math.Clamp(point.X - Neighbourhood, desk.Left, Math.Max(desk.Left, desk.Right - 1));
        var top = Math.Clamp(point.Y - Neighbourhood, desk.Top, Math.Max(desk.Top, desk.Bottom - 1));
        var right = Math.Clamp(point.X + Neighbourhood + 1, left + 1, desk.Right);
        var bottom = Math.Clamp(point.Y + Neighbourhood + 1, top + 1, desk.Bottom);

        var shot = Shot(left, top, right - left, bottom - top, window);
        Saw(step, LookKind.Pixel, shot.Frame, shot.Origin,
            [
                new(new ImageMatch(1, shot.Origin,
                    new ScreenSize(shot.Frame.Width, shot.Frame.Height)), LookRole.Area),
                new(new ImageMatch(1, point, new ScreenSize(1, 1)), LookRole.Target),
            ],
            2, looking);
    }

    /// <summary>How far around a watched pixel the look reaches, so the pixel has some context.</summary>
    private const int Neighbourhood = 60;

    /// <summary>The marks for a look: where it looked, and everything that was found there.</summary>
    private static List<LookBox> Marks(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen)
    {
        var marks = new List<LookBox>();
        foreach (var (area, origin) in areas)
        {
            marks.Add(new LookBox(
                new ImageMatch(0, origin, new ScreenSize(area.Width, area.Height)), LookRole.Area));
        }

        for (var index = 0; index < found.Count; index++)
        {
            marks.Add(new LookBox(
                found[index].Match,
                index + 1 == chosen ? LookRole.Hit : LookRole.Candidate,
                found[index].Label));
        }

        return marks;
    }

    /// <summary>
    /// The picture to draw a step's marks on: the single area it looked at, or the whole of what
    /// it looked at when there are several. Several are laid out of the areas themselves rather
    /// than read off the screen again — where the areas came from is not always the screen, and a
    /// step that reads a window it does not cover would otherwise be drawn over a picture of
    /// whatever is on top of it.
    /// </summary>
    private static (ImageFrame Frame, ScreenPoint Origin) Picture(
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas)
    {
        if (areas.Count == 1)
        {
            return areas[0];
        }

        var left = areas.Min(area => area.Origin.X);
        var top = areas.Min(area => area.Origin.Y);
        var width = Math.Max(1, areas.Max(area => area.Origin.X + area.Frame.Width) - left);
        var height = Math.Max(1, areas.Max(area => area.Origin.Y + area.Frame.Height) - top);
        var pixels = new byte[(long)width * height * 4];
        foreach (var (frame, origin) in areas)
        {
            for (var row = 0; row < frame.Height; row++)
            {
                Array.Copy(frame.Bgra, (long)row * frame.Width * 4, pixels,
                    ((long)(origin.Y - top + row) * width + (origin.X - left)) * 4,
                    (long)frame.Width * 4);
            }
        }

        return (new ImageFrame(width, height, pixels), new ScreenPoint(left, top));
    }
}
