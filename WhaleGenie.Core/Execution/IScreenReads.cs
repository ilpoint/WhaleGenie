using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Reading the screen: where a step searches, what a picture reads as, which readings it would act
/// on, and the look left for whoever is watching. The rules are the same for a step that reads text
/// and a condition that asks whether some text is there, so both come through here rather than each
/// keeping its own copy.
/// </summary>
internal interface IScreenReads
{
    /// <summary>The places a step searches, each a picture with the corner it starts at.</summary>
    IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> Areas(ExecutableStep step);

    /// <summary>
    /// What the step's search areas read as, with the positions in screen pixels, together with the
    /// areas themselves so the look can show where it was taken from.
    /// </summary>
    (IReadOnlyList<TextSpan> Spans, IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> Areas)
        Read(ExecutableStep step, CancellationToken token);

    /// <summary>The readings a step would act on, and which of them it would pick, counted from one.</summary>
    (List<TextSpan> Hits, int Chosen) Counting(ExecutableStep step,
        IReadOnlyList<TextSpan> readings, string wanted, string mode);

    /// <summary>Waits out the run's frame gap, so two looks do not read the same frame twice.</summary>
    Task WaitForScreen(CancellationToken token);

    /// <summary>Runs a look and hands what it saw to whoever is watching.</summary>
    void Showing(Action body);

    /// <summary>The same, for a step that has to wait for what it is looking at.</summary>
    Task Showing(Func<Task> body);

    /// <summary>
    /// Hands over what a search saw: the areas searched, every place the thing was found, and the
    /// one the step went with.
    /// </summary>
    void SawSearch(ExecutableStep step, LookKind kind,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas,
        IReadOnlyList<(ImageMatch Match, string? Label)> found, int chosen, string wanted = "",
        double? minimum = null, string? note = null);

    /// <summary>Clicks where a step just worked out, and marks it on the look it came from.</summary>
    void ClickAt(ExecutableStep step, ScreenPoint at);

    /// <summary>The beat between two looks at the screen.</summary>
    int Beat();
}
