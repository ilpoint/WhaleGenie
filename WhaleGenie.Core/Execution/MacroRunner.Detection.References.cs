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
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// The pictures a step looks for, and the one it is given.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The pictures a step looks for, in the order it listed them. A picture the step did not fill
    /// in is left out rather than looked for; a step that lists none is a step that cannot look for
    /// anything, and says so.
    /// </summary>
    private IReadOnlyList<WantedPicture> References(ExecutableStep step)
    {
        var wanted = new List<WantedPicture>();
        foreach (var row in step.Rows("images"))
        {
            var written = Word(row, "image");
            if (written.Length > 0)
            {
                wanted.Add(new WantedPicture(written, Picture(written)));
            }
        }

        return wanted.Count > 0 ? wanted : throw new StepFailure(RunKeys.MissingImage, string.Empty);
    }

    /// <summary>One reference picture of a step: how the step wrote it down, and the picture itself.</summary>
    private sealed record WantedPicture(string Name, ImageFrame Frame);

    /// <summary>
    /// The one reference picture a step names, for the steps that hold a single one rather than a
    /// list: something an earlier Capture saved, or a file on disk.
    /// </summary>
    private ImageFrame Reference(ExecutableStep step)
    {
        var text = ReadText(step, "imageSource").Trim();
        if (text.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingImage, string.Empty);
        }

        return Picture(text);
    }

    /// <summary>
    /// A picture the step wrote down: what an earlier Capture saved, or a file on disk. The picture
    /// is kept beside the time the file was last written, so a macro that looks for the same picture
    /// again — in a loop, or every time it polls while it waits — uses the one it already has
    /// instead of reading and decoding the file again.
    /// </summary>
    private ImageFrame Picture(string text)
    {
        // A picture an earlier Capture saved, named with or without the dollar sign so it reads
        // the same as every other field a variable can go in.
        var named = text.StartsWith('$') ? text[1..].Trim() : text;
        if (_images.TryGetValue(named, out var captured))
        {
            return captured;
        }

        // Otherwise it is a file, either written out or held in a variable.
        var path = Read(text).AsText();
        var written = Written(path);
        if (written is { } stamp
            && _references.TryGetValue(path, out var kept)
            && kept.Written == stamp)
        {
            return kept.Frame;
        }

        var picture = _devices.Vision.Load(path) ?? throw new StepFailure(RunKeys.MissingImage, text);
        if (written is not null)
        {
            _references[path] = (written.Value, picture);
        }

        return picture;
    }

    /// <summary>
    /// When a file was last written, or null when it cannot be asked — a picture held in a
    /// variable, or a path this run has no business reading. Nothing is kept for those.
    /// </summary>
    private static DateTime? Written(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or ArgumentException or NotSupportedException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
