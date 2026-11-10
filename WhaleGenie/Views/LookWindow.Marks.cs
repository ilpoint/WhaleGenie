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

// One mark in words, and putting the light on one in the list.
public partial class LookWindow : AppWindow
{
    /// <summary>
    /// Where a mark is on the screen, in the numbers the step's own fields take. The middle comes
    /// first: that is the point a click and a drag are aimed at, it is what a hit's own record
    /// carries, and the arithmetic behind it is the engine's (<see cref="ImageMatch.Center"/> is
    /// the same integer division the record's x and y come from). The top left and the size follow,
    /// for the fields that ask for a rectangle rather than a place.
    /// </summary>
    private static string Detail(LookBox box)
    {
        var centre = box.Match.Center;
        var at = string.Create(CultureInfo.CurrentCulture, $"({centre.X}, {centre.Y})");
        if (box.Role == LookRole.Target)
        {
            return Strings.Format("Look.Centre", at);
        }

        var corner = string.Create(CultureInfo.CurrentCulture,
            $"{box.Match.Location.X}, {box.Match.Location.Y}");
        return Strings.Format("Look.Box", at, corner, box.Match.Size.Width, box.Match.Size.Height);
    }

    /// <summary>
    /// How far one candidate was from the score the step asked for, or null when it is not a
    /// candidate at all or the step never named a score: a search area was never compared with
    /// anything, and a step without a threshold has nothing for a mark to fall short of.
    /// </summary>
    private string? Short(LookBox box)
    {
        if (_look.Minimum is not { } needed
            || box.Role is not (LookRole.Candidate or LookRole.Hit)
            || box.Match.Score >= needed)
        {
            return null;
        }

        return Strings.Format("Look.Short", Score(needed - box.Match.Score, _look.Kind));
    }

    private void Light(int index, bool on)
    {
        if (_drawn.Count <= index)
        {
            return;
        }

        var (box, visual) = _drawn[index];
        visual.BorderBrush = on ? Brushes.White : Line(box.Role);
    }

    /// <summary>Brings one mark into the middle of the window, which is how a list of ten hits is read.</summary>
    private void Bring(int index)
    {
        if (_drawn.Count <= index || _scroll is null || _board is null)
        {
            return;
        }

        var (_, visual) = _drawn[index];
        var place = LookGeometry.Place(_look.Origin, _drawn[index].Box.Match.Location,
            _drawn[index].Box.Match.Size, _zoom);
        var middle = new Vector(place.X + (place.Width / 2), place.Y + (place.Height / 2));
        _scroll.Offset = middle
            - new Vector(_scroll.Viewport.Width / 2, _scroll.Viewport.Height / 2);
        visual.BorderThickness = new Thickness(3);
    }
}
