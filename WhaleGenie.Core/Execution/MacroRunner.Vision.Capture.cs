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

// Taking a picture: from the screen or a window, and the rectangle a step names.
public sealed partial class MacroRunner
{
    /// <summary>Copies a region of the screen into a variable the macro can look at again.</summary>
    private void Capture(ExecutableStep step, int depth)
    {
        var (frame, corner) = CaptureBox(step);
        Keep(step, depth, frame, corner);
    }

    /// <summary>
    /// The rectangle a capture step covers, taken off the screen. The corner is worked out first so
    /// that the rectangle reported is the one on screen, which is what a later "region" naming this
    /// picture has to read.
    /// </summary>
    private (ImageFrame Frame, ScreenPoint Corner) CaptureBox(ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        var (corner, width, height) = Box(step, anchor.Corner, anchor.Window);
        var shot = Shot(corner.X, corner.Y, width, height, anchor.Window);
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
    private (ScreenPoint Corner, int Width, int Height) Box(ExecutableStep step, ScreenPoint? origin,
        WindowInfo? window)
    {
        if (RecordedRect(step, "rect") is { } sides)
        {
            return (new ScreenPoint(sides.Left, sides.Top), sides.Width, sides.Height);
        }

        var corner = Placed(origin, Number(step, "x"), Number(step, "y"));
        int right;
        int bottom;
        if (window is { } target)
        {
            var originOfWindow = _devices.Windows.ClientOrigin(target.Handle);
            var size = _devices.Windows.ClientSize(target.Handle);
            right = originOfWindow.X + size.Width;
            bottom = originOfWindow.Y + size.Height;
        }
        else
        {
            var desk = _devices.Screen.Desktop.Bounds;
            right = desk.Right;
            bottom = desk.Bottom;
        }

        return (corner,
            Reach(step, FieldNames.Width, corner.X, right),
            Reach(step, FieldNames.Height, corner.Y, bottom));
    }

    /// <summary>
    /// The rectangle a record field names, or null when nobody filled it in. A value written there
    /// that is not a record carrying all four sides is refused with what was written.
    /// </summary>
    private (int Left, int Top, int Width, int Height)? RecordedRect(ExecutableStep step, string name)
    {
        var value = ReadValue(step, name);
        if (value.IsNull)
        {
            return null;
        }

        var written = step.Text(name).Trim();
        return value.IsRecord && Sides(value) is { } sides
            ? sides
            : throw new StepFailure(RunKeys.BadRegion, written);
    }

    /// <summary>
    /// One side of the rectangle: the number the step wrote, or — for a side whose empty cell the
    /// catalogue calls "to the edge" — the rest of the screen from where the rectangle starts.
    /// </summary>
    private int Reach(ExecutableStep step, string name, int from, int limit)
        => !Given(step, name) && Empties(step, name) == ActionEmptyMeans.ToEdge
            ? Math.Max(1, limit - from)
            : Math.Max(1, Number(step, name));

    /// <summary>
    /// Copies a whole window into a variable. The window's own rectangle is what is copied, so the
    /// title bar and the border come with it, and a window that is not in front can be read too.
    /// </summary>
    private void CaptureWindow(ExecutableStep step, int depth)
    {
        var (frame, corner, window) = WindowBox(step);
        Keep(step, depth, frame, corner, window);
    }

    /// <summary>The whole of the window a step names, taken off the screen.</summary>
    private (ImageFrame Frame, ScreenPoint Corner, WindowInfo Window) WindowBox(ExecutableStep step)
    {
        var window = Locate(step);
        var shot = Shot(window.Location.X, window.Location.Y, window.Size.Width,
            window.Size.Height, window);
        return (shot.Frame, shot.Origin, window);
    }
}
