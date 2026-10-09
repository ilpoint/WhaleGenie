using System.Collections.Generic;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>What sort of looking a step did, which decides how its numbers read.</summary>
public enum LookKind
{
    /// <summary>A reference picture was looked for. Scores are confidences, from zero to one.</summary>
    Template,

    /// <summary>A colour was looked for. Scores say how close each pixel came, one being exact.</summary>
    Colour,

    /// <summary>Writing was read. The score is the reading model's own and has no fixed range.</summary>
    Text,

    /// <summary>A picture was taken of part of the screen. There is nothing to score.</summary>
    Capture,

    /// <summary>One pixel was watched, with a square of screen around it to look at.</summary>
    Pixel,
}

/// <summary>What one mark on the picture stands for.</summary>
public enum LookRole
{
    /// <summary>Where the step looked: a search area, or the rectangle a capture covered.</summary>
    Area,

    /// <summary>Somewhere the thing was found, but not the one the step went with.</summary>
    Candidate,

    /// <summary>The one the step went with.</summary>
    Hit,

    /// <summary>Where the step acted, or is about to act — the point a click is aimed at.</summary>
    Target,
}

/// <summary>One mark drawn on the picture a step looked at.</summary>
/// <remarks>
/// The geometry and the score are the ones the engine worked with, already in screen pixels. A
/// mark that stands for a search area carries no score: nothing was compared there.
/// </remarks>
public sealed record LookBox(ImageMatch Match, LookRole Role, string? Label = null);

/// <summary>
/// What one step saw when it looked at the screen: the picture itself, where that picture sits on
/// the screen, and every mark on it.
/// </summary>
/// <remarks>
/// The engine hands this over as it stands rather than drawing anything — the interface owns the
/// drawing, and a run nobody is watching pays nothing for it. A step that looked more than once,
/// such as one that waited, hands over the last look: that is the one worth seeing when a step did
/// not find what it wanted.
/// </remarks>
public sealed record StepLook
{
    /// <summary>The step that looked, named the way breakpoints and step results name it.</summary>
    public required string StepId { get; init; }

    public required string StepType { get; init; }

    public required LookKind Kind { get; init; }

    /// <summary>The picture that was looked at, in the layout the rest of the engine uses.</summary>
    public required ImageFrame Frame { get; init; }

    /// <summary>Where the top left corner of <see cref="Frame"/> sits on the screen.</summary>
    public required ScreenPoint Origin { get; init; }

    public IReadOnlyList<LookBox> Boxes { get; init; } = [];

    /// <summary>
    /// Which mark is the one the step went with, counted from one inside <see cref="Boxes"/>, or
    /// zero when the step went with none of them.
    /// </summary>
    public int ChosenIndex { get; init; }

    /// <summary>What the step was after, as the user wrote it: a picture, a colour, some writing.</summary>
    public string Looking { get; init; } = string.Empty;

    /// <summary>
    /// The score a hit had to reach to count, in the same units as the marks carry, or null when
    /// the step was not asking for a score at all. It is what turns "the best was 0.62" into "the
    /// best was 0.62 and it needed 0.9".
    /// </summary>
    public double? Minimum { get; init; }

    /// <summary>The reference picture itself, for the steps that looked for one.</summary>
    public ImageFrame? Needle { get; init; }

    /// <summary>Anything else about this look worth saying, such as the writing that was read.</summary>
    public string? Note { get; init; }
}

/// <summary>Where steps hand over what they saw, for a debugger to draw.</summary>
/// <remarks>
/// Kept apart from <see cref="IRunHost"/> on purpose: a macro a trigger started has nobody
/// watching it, and building a picture for every step of it would only cost time. A runner that
/// is given no looks therefore builds none.
/// </remarks>
public interface IRunLooks
{
    void Look(StepLook look);
}

/// <summary>
/// What came of looking once: the look itself, or the message key and its detail saying why the
/// looking could not be done at all. The key travels as a key the way every other run failure
/// does, because the engine has no language.
/// </summary>
public sealed record LookOutcome(StepLook? Look, string Key = "", string Detail = "");
