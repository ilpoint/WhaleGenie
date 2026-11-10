using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.Views;

// What the window says about the look: where it was, how it went, the score, and the zoom it fits at.
public partial class LookWindow : AppWindow
{
    private void Set(string name, string text)
    {
        // Asked for by name rather than by type: the lookup that names a type fails loudly when the
        // control is another one, and the note is a box while the lines above it are text.
        switch (this.FindControl<Control>(name))
        {
            case TextBlock block:
                block.Text = text;
                break;
            case TextBox box:
                box.Text = text;
                break;
        }
    }

    /// <summary>
    /// Which window the picture came out of and how it was taken, beside what the step was after.
    /// A step that found nothing is answered by this line: it says whether the step was reading the
    /// right window at all, which the picture underneath it cannot say on its own.
    /// </summary>
    private static string Where(StepLook look)
    {
        if (look.Method == CaptureWay.Auto)
        {
            return string.Empty;
        }

        var source = look.Source.Length == 0
            ? Strings.Get("Look.Source.desktop")
            : Strings.Format("Look.Source.window", look.Source);

        return Strings.Format("Look.Where", source,
            Strings.Get("Look.Way." + CaptureWayNames.Written(look.Method)));
    }

    /// <summary>
    /// How the step went, in a line: found or not, the best score on the picture, and — when the
    /// step wanted a score and did not get one — what it would have taken.
    /// </summary>
    private string Outcome(StepLook look)
    {
        if (look.Kind == LookKind.Capture)
        {
            return Strings.Get("Look.Outcome.Capture");
        }

        if (look.Kind == LookKind.Clipboard)
        {
            return Strings.Get("Look.Outcome.Clipboard");
        }

        if (look.Kind == LookKind.Pixel)
        {
            return Strings.Get("Look.Outcome.Pixel");
        }

        var best = look.Boxes
            .Where(box => box.Role is LookRole.Candidate or LookRole.Hit)
            .Select(box => box.Match.Score)
            .DefaultIfEmpty(double.NaN)
            .Max();

        if (double.IsNaN(best))
        {
            return Strings.Get("Look.Outcome.Nothing");
        }

        if (look.ChosenIndex > 0)
        {
            return Strings.Format("Look.Outcome.Hit", Score(best, look.Kind));
        }

        var line = Strings.Format("Look.Outcome.Miss", Score(best, look.Kind));
        return look.Minimum is { } needed
            ? line + Strings.Format("Look.Needed", Score(needed, look.Kind))
            : line;
    }

    /// <summary>
    /// One score as a person reads it. A picture match is a confidence, so it is a percentage; a
    /// reading of writing is the model's own number and has no range to be a percentage of.
    /// </summary>
    private static string Score(double value, LookKind kind) => kind switch
    {
        LookKind.Template or LookKind.Detection
            => value.ToString("P0", CultureInfo.CurrentCulture),
        LookKind.Colour => value.ToString("0.00", CultureInfo.CurrentCulture),
        _ => value.ToString("0.#", CultureInfo.CurrentCulture),
    };

    // --------------------------------------------------------------------- picture

    /// <summary>The zoom that shows the whole picture in the room the window has for it.</summary>
    private double Fit()
    {
        var room = _scroll?.Viewport ?? default;
        return LookGeometry.Fit(_look.Frame.Width, _look.Frame.Height,
            (room.Width > 0 ? room.Width : 640) - 8, (room.Height > 0 ? room.Height : 400) - 8);
    }

    /// <summary>
    /// Fits the picture to the room there really is for it, once the window has been laid out or
    /// changed size. It is the correction to the fit worked out when the window was built, which
    /// had no room to measure yet. A size the person chose by hand is left alone: the picture was
    /// zoomed in on purpose, and moving the window is not asking for that to be undone.
    /// </summary>
    private void FitToRoom()
    {
        if (_fitted)
        {
            Zoom(Fit(), false, fitted: true);
        }
    }

    /// <summary>The zoom the picture is drawn at right now, as a number rather than as the label.</summary>
    internal double ZoomLevel => _zoom;

    private void Zoom(double zoom, bool keepPlace, bool fitted = false)
    {
        var was = _zoom;
        _zoom = Math.Clamp(zoom, LookGeometry.SmallestZoom, LookGeometry.LargestZoom);
        _fitted = fitted;

        if (_zoomText is not null)
        {
            _zoomText.Text = (_zoom * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
        }

        Draw();

        // What was in the middle stays in the middle, so zooming in on a mark does not move it
        // off the window.
        if (keepPlace && _scroll is not null && was > 0)
        {
            var half = new Vector(_scroll.Viewport.Width / 2, _scroll.Viewport.Height / 2);
            var middle = _scroll.Offset + half;
            var scaled = middle * (_zoom / was);
            _scroll.Offset = scaled - half;
        }
    }
}
