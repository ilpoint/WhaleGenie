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

// Keeping a picture a step took: in a variable, on disk, and the pixel read out of one.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Keeps a picture under the name a step gave it, with the rectangle it covers beside it, so a
    /// later step can search inside it or hand the region to something else. The size written down
    /// is the picture's own rather than the size that was asked for: a capture that ran off the
    /// edge of the screen comes back smaller, and a search region that is bigger than the picture
    /// would look at pixels that are not there.
    /// </summary>
    private void Keep(ExecutableStep step, int depth, ImageFrame frame, ScreenPoint corner,
        WindowInfo? sourceWindow = null)
    {
        // The picture is kept under the name of the image result, and only there: a step that was
        // told not to hand the picture over leaves nothing for a later step to search in, which is
        // what leaving that result unticked means.
        var name = Give(step, "image", Value.FromImage($"<image {frame.Width}x{frame.Height}>"));
        if (name.Length > 0)
        {
            _images[name] = frame;
        }

        GiveTaken(step);

        // Where the picture sits is one record beside the picture, so it can be handed to a search
        // region as it stands or read one part at a time. The middle is in it beside the corner:
        // that is the point a step aims at, and a macro that means the middle of what it just took
        // should not have to work it out of two halves of the rectangle.
        var middle = Middle(corner, new ScreenSize(frame.Width, frame.Height));
        ScreenPoint? client = null;
        if (sourceWindow is { } source)
        {
            var origin = _devices.Windows.ClientOrigin(source.Handle);
            client = new ScreenPoint(middle.X - origin.X, middle.Y - origin.Y);
        }
        GivePoints(step, middle, client);
        Give(step, "place", Value.FromRecord(
        [
            (FieldNames.X, Value.FromNumber(middle.X)),
            (FieldNames.Y, Value.FromNumber(middle.Y)),
            (FieldNames.Left, Value.FromNumber(corner.X)),
            (FieldNames.Top, Value.FromNumber(corner.Y)),
            (FieldNames.Width, Value.FromNumber(frame.Width)),
            (FieldNames.Height, Value.FromNumber(frame.Height)),
            (FieldNames.Box, Value.FromText($"{corner.X},{corner.Y},{frame.Width},{frame.Height}")),
        ]));

        Saw(step, LookKind.Capture, frame, corner,
            [new LookBox(new ImageMatch(0, corner, new ScreenSize(frame.Width, frame.Height)), LookRole.Area)],
            1, name);
        if (name.Length > 0)
        {
            Log(LogLevel.Info, depth, step.Type, LogKeys.Capture, name, $"{frame.Width}x{frame.Height}");
        }

        KeepFile(step, depth, frame);
        Give(step, "path", Value.FromText(SavedPath(step)));
    }

    /// <summary>
    /// Writes the picture out where the step asked for a file, when it asked for one: one step takes
    /// the picture and keeps it, rather than a capture followed by a step that writes what it left
    /// behind. It is always a PNG, which is the small end of what a picture can be written as, and a
    /// file of that name is replaced — the same answer every other step that writes a file gives.
    /// </summary>
    private void KeepFile(ExecutableStep step, int depth, ImageFrame frame)
    {
        var path = PathOf(step);
        if (path.Trim().Length == 0)
        {
            return;
        }

        _devices.Files.WriteBytes(path, PngWriter.Encode(frame));
        Log(LogLevel.Info, depth, step.Type, LogKeys.SavedPicture, SavedPath(step),
            $"{frame.Width}x{frame.Height}");
    }

    /// <summary>
    /// The file a capture wrote, in the form a later step can open: the macros folder is where a
    /// relative path is measured from, and the result should say the same place that was written.
    /// </summary>
    private string SavedPath(ExecutableStep step)
    {
        var path = PathOf(step);
        return path.Trim().Length == 0
            ? string.Empty
            : _devices.Files.Resolve(path);
    }

    private void GetPixel(ExecutableStep step, int depth)
    {
        var (point, window) = Spot(step);
        var colour = PixelAt(step, point, window);
        GivePointAt(step, point, window);

        // The same colour is handed over in both readings the action has always had: the text a
        // person reads off a colour picker and the whole number a macro can compare. Neither is a
        // switch any more, so a step that ticks one does not have to know how the other is spelled.
        Give(step, "color", Value.FromText(colour.ToHex()));
        Give(step, "number", Value.FromNumber((colour.R) | (colour.G << 8) | (colour.B << 16)));

        SawPixel(step, point, colour.ToHex(), window);
        Log(LogLevel.Info, depth, step.Type, LogKeys.SawColor, colour.ToHex());
    }
}
