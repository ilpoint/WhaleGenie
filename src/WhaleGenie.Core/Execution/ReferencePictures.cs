using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The pictures a step looks for: what an earlier capture saved under a name, or a file on disk,
/// kept beside the time the file was last written so a search in a loop reads it once rather than
/// every time round.
/// </summary>
internal sealed class ReferencePictures(IDeviceLayer devices, VariableStore variables,
    RunState run, EvaluationContext evaluation)
{
    /// <summary>
    /// The pictures a step looks for, in the order it listed them. A picture the step did not fill
    /// in is left out rather than looked for; a step that lists none is a step that cannot look for
    /// anything, and says so.
    /// </summary>
    public IReadOnlyList<WantedPicture> All(ExecutableStep step)
    {
        var wanted = new List<WantedPicture>();
        foreach (var row in step.Rows("images"))
        {
            var written = row.GetValueOrDefault("image") ?? Formula.Nothing;
            var name = ShapeWords.Of(written);
            if (name.Length > 0)
            {
                wanted.Add(new WantedPicture(name, Picture(written)));
            }
        }

        return wanted.Count > 0 ? wanted : throw new StepFailure(RunKeys.MissingImage, string.Empty);
    }

    /// <summary>
    /// The one reference picture a step names, for the steps that hold a single one rather than a
    /// list: something an earlier Capture saved, or a file on disk.
    /// </summary>
    public ImageFrame One(ExecutableStep step)
    {
        var written = step.Written("imageSource");
        if (Formula.IsNothing(written))
        {
            throw new StepFailure(RunKeys.MissingImage, string.Empty);
        }

        return Picture(written);
    }

    /// <summary>
    /// A picture the step wrote down: what an earlier Capture saved, or a file on disk. The picture
    /// is kept beside the time the file was last written, so a macro that looks for the same picture
    /// again — in a loop, or every time it polls while it waits — uses the one it already has
    /// instead of reading and decoding the file again.
    /// </summary>
    public ImageFrame Picture(ExpressionNode value)
    {
        // A picture an earlier Capture saved is looked up by the word the field wrote, whether the
        // shape is a name or plain text. Anything that is not one already is worked out as a path.
        if (run.Images.TryGetValue(ShapeWords.Of(value), out var captured))
        {
            return captured;
        }

        var path = Expression.Evaluate(value, variables, evaluation).AsText();
        var written = Written(path);
        if (written is { } stamp
            && run.References.TryGetValue(path, out var kept)
            && kept.Written == stamp)
        {
            return kept.Frame;
        }

        var picture = devices.Vision.Load(path)
            ?? throw new StepFailure(RunKeys.MissingImage, Formula.Speak(value));
        if (written is not null)
        {
            run.References[path] = (written.Value, picture);
        }

        return picture;
    }

    /// <summary>
    /// When a file was last written, asked of the machine through the device contract rather than
    /// read where the answer is used: a picture held in a variable, or a path this run has no
    /// business reading, answers nothing and is not kept.
    /// </summary>
    private DateTimeOffset? Written(string path) => devices.Files.LastWritten(path);
}

/// <summary>One reference picture of a step: how the step wrote it down, and the picture itself.</summary>
internal sealed record WantedPicture(string Name, ImageFrame Frame);
