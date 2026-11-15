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

// What a step leaves behind when it looks: the frame it saw, the search it made, and the marks a window shows.
public sealed partial class MacroRunner
{
    // -------------------------------------------------------------------- looks

    /// <summary>
    /// Runs one step that looks at the screen and hands over what it saw, whatever came of it: the
    /// look of a step that found nothing is the one worth having.
    /// </summary>
    private void Watching(Action body)
    {
        _here.Saw = null;
        try
        {
            body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>The same, for a step that has to wait for what it is looking at.</summary>
    private async Task Watching(Func<Task> body)
    {
        _here.Saw = null;
        try
        {
            await body();
        }
        finally
        {
            Show();
        }
    }

    /// <summary>Hands the look over to whoever is watching, and forgets it.</summary>
    private void Show()
    {
        var look = _here.Saw;
        _here.Saw = null;
        if (_looks is null || look is null)
        {
            return;
        }

        _looks.Look(look);
    }

    /// <summary>
    /// How the marks are built, and what sort of looking this was. Nothing is built when nobody is
    /// watching: a picture of every step of a run nobody is looking at is work for nothing, and a
    /// step that waits would build one every two hundred milliseconds.
    /// </summary>
    private void Saw(ExecutableStep step, LookKind kind, ImageFrame frame, ScreenPoint origin,
        IReadOnlyList<LookBox> boxes, int chosen, string looking = "", ImageFrame? needle = null,
        string? note = null, double? minimum = null, int picture = 0, int pictures = 0)
    {
        if (_looks is null)
        {
            return;
        }

        _here.Saw = new StepLook
        {
            StepId = step.Id,
            StepType = step.Type,
            Kind = kind,
            Frame = frame,
            Origin = origin,
            Boxes = boxes,
            ChosenIndex = chosen,
            Looking = looking,
            Needle = needle,
            Note = note,
            Minimum = minimum,
            PictureNumber = picture,
            PictureCount = pictures,
            Source = _here.TakenWindow,
            Method = _here.TakenMethod ?? CaptureWay.Auto,
        };
    }

    /// <summary>
    /// What a step that looked for something saw: the areas it searched, every place the thing was
    /// found, and the one it went with. Several areas are put together into one picture so that
    /// every mark has somewhere to sit.
    /// </summary>
    private void SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string looking = "",
        ImageFrame? needle = null, double? minimum = null, int picture = 0, int pictures = 0)
    {
        if (_looks is null)
        {
            return;
        }

        var (frame, origin) = Picture(areas);
        Saw(step, kind, frame, origin, Marks(areas, found, chosen),
            chosen == 0 ? 0 : areas.Count + chosen, looking, needle, null, minimum, picture, pictures);
    }
}
