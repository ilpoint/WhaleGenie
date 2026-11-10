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

// Looking at the screen once without acting: the anchor a step reads from, its pixels, and what a trial sees.
public sealed partial class MacroRunner
{
    /// <summary>
    /// The same, together with the window those numbers were counted from when there was one. The
    /// window is also where the step's pixels are to be read from: a step that says "a hundred
    /// pixels into the game window" and a step that says "read the game window" are saying the same
    /// thing about which window, and one of them should not have to say it twice.
    /// </summary>
    private (ScreenPoint? Corner, WindowInfo? Window) AnchorOf(ExecutableStep step)
    {
        var mode = ReadText(step, "anchorMode").Trim().ToLowerInvariant();
        if (mode.Length > 0 && mode is not ("screen" or "window"))
        {
            throw new StepFailure(RunKeys.BadOption, "anchorMode");
        }

        if (mode != "window")
        {
            return (null, null);
        }

        // The window is recognised by the same captured rule every window action reads.
        var rule = WindowRuleOf(step);
        if (!rule.HasTarget)
        {
            var front = _devices.Windows.Foreground();
            return (front is { } there ? _devices.Windows.ClientOrigin(there.Handle) : null, front);
        }

        var window = _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
                         rule.Options)
                     ?? throw new StepFailure(RunKeys.WindowNotFound, rule.MatchValue);

        var corner = _devices.Windows.ClientOrigin(window.Handle);
        return (corner, window);
    }

    /// <summary>One written position, in the screen pixels the devices ask for.</summary>
    private ScreenPoint Place(ExecutableStep step, int x, int y) => Placed(Anchor(step), x, y);

    private static ScreenPoint Placed(ScreenPoint? origin, int x, int y)
        => origin is { } corner ? new ScreenPoint(corner.X + x, corner.Y + y) : new ScreenPoint(x, y);

    /// <summary>The colour of the pixel a step points at, counted from wherever it anchors.</summary>
    private PixelColor PixelAt(ExecutableStep step)
    {
        var (corner, window) = Spot(step);
        return PixelAt(step, corner, window);
    }

    /// <summary>
    /// The colour of one pixel. A pixel the step counts from the screen is read straight off the
    /// screen, which is a fraction of the cost of a picture; one it counts from a window is read
    /// out of a picture of that window, so a window another window covers still gives its own
    /// colour rather than the covering window's.
    /// </summary>
    private PixelColor PixelAt(ExecutableStep step, ScreenPoint point, WindowInfo? window)
    {
        if (window is null)
        {
            // A pixel of the desktop is read straight off it rather than out of a picture, so this
            // is the one reading that has to say for itself that it is not reading a window.
            Reading(null);
            return _devices.Screen.PixelAt(point.X, point.Y);
        }

        var shot = Shot(point.X, point.Y, 1, 1, window);
        if (shot.Origin != point)
        {
            // The picture came back from somewhere else, which means the window does not reach
            // that far: there is no such pixel, and reading a neighbour's colour would be a lie.
            throw new StepFailure(RunKeys.PixelOutsideScreen, $"{point.X},{point.Y}");
        }

        return shot.Frame[0, 0];
    }

    /// <summary>
    /// Whether this kind of step is one that looks at the screen, and so is one whose looking can
    /// be tried out on its own. It is what the dialog offers "try looking" for, and the only thing
    /// <see cref="LookOnce"/> will look at the screen for.
    /// </summary>
    /// <summary>
    /// Whether a trial can look at the screen for this kind of step. It is read off the catalogue
    /// rather than kept as a list here: the list that used to live here had drifted, and a step the
    /// catalogue says looks at the screen was told it does not.
    /// </summary>
    public static bool CanLook(string stepType)
        => ActionSemanticsCatalog.Find(stepType)?.LooksAtScreen == true;

    /// <summary>
    /// Looks at the screen the way a step would and hands back what it saw, without doing anything
    /// about it: no key is pressed, no mouse button goes down, no variable is written. It answers
    /// "what does this step actually see", where running the step would answer it by clicking on
    /// the user's screen. Null when this kind of step does not look at the screen at all.
    /// <paramref name="watch"/> is told which window is being read as it happens, if anybody wants
    /// to be: it is the same callback a run hands over, so a trial can show what a run shows.
    /// </summary>
    public static LookOutcome LookOnce(ExecutableStep step, IDeviceLayer devices,
        IRunLooks? watch = null)
    {
        if (!CanLook(step.Type))
        {
            return new LookOutcome(null, RunKeys.NotALookingStep);
        }

        var asked = new Asked(watch);
        try
        {
            new MacroRunner(new VariableStore(), devices: devices, looks: asked).SeeOnce(step);
        }
        catch (ExpressionException failure)
        {
            return new LookOutcome(null, RunKeys.BadExpression, failure.Detail);
        }
        catch (RegexTimeoutException failure)
        {
            return new LookOutcome(null, RunKeys.RegexTimeout, failure.Pattern);
        }
        catch (DeviceUnavailableException failure)
        {
            return new LookOutcome(null, RunKeys.NoDevice, failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new LookOutcome(null, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while looking, the answer is a message rather than
            // an exception in the dialog's face.
            return new LookOutcome(null, RunKeys.DeviceFailed, failure.Message);
        }

        return asked.Seen is { } look
            ? new LookOutcome(look)
            : new LookOutcome(null, RunKeys.NotALookingStep);
    }

    /// <summary>
    /// Answers one condition the way a run would, on the values in play at that moment and without
    /// carrying anything out. This is what a breakpoint's own condition is asked with: the debugger
    /// has the values the run has just written, and none of the run's own machinery. A condition
    /// that cannot be answered at all comes back saying why rather than throwing, so the caller can
    /// tell "no" apart from "could not be asked".
    /// </summary>
    public static ConditionOutcome AskCondition(ExecutableStep condition, VariableStore variables,
        IDeviceLayer devices, IMacroLibrary? macros = null)
    {
        try
        {
            return new ConditionOutcome(new MacroRunner(variables, devices: devices, macros: macros)
                .Check(condition, "condition", CancellationToken.None));
        }
        catch (ExpressionException failure)
        {
            return new ConditionOutcome(false, RunKeys.BadExpression, failure.Detail);
        }
        catch (RegexTimeoutException failure)
        {
            return new ConditionOutcome(false, RunKeys.RegexTimeout, failure.Pattern);
        }
        catch (DeviceUnavailableException failure)
        {
            return new ConditionOutcome(false, RunKeys.NoDevice, failure.Capability);
        }
        catch (DeviceActionException failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (StepFailure failure)
        {
            return new ConditionOutcome(false, failure.Key, failure.Detail);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Whatever a device driver throws while a condition is asked, the answer is a message
            // rather than an exception in the debugger's face.
            return new ConditionOutcome(false, RunKeys.DeviceFailed, failure.Message);
        }
    }

    /// <summary>Where a look goes when the caller only wanted the one.</summary>
    private sealed class Asked(IRunLooks? watch) : IRunLooks
    {
        public StepLook? Seen { get; private set; }

        public void Look(StepLook look) => Seen = look;

        /// <summary>Nothing is read from what a step came to; only the one look was wanted.</summary>
        public void Ran(StepRun run)
        {
        }

        public void Reading(WindowInfo? window) => watch?.Reading(window);
    }

    /// <summary>
    /// One look at the screen for a step, without the step: a search looks once rather than
    /// waiting, a wait does not wait, and the mouse is left alone.
    /// </summary>
    private void SeeOnce(ExecutableStep step)
    {
        switch (step.Type)
        {
            case "vision.findImage":
            case "vision.waitImage":
            case "vision.clickImage":
                Watching(() => Hits(step));
                break;

            case "vision.detect":
            case "vision.waitDetect":
            case "vision.clickDetect":
                Watching(() => Sightings(step));
                break;

            case "vision.findColor":
            case "vision.waitColor":
            case "vision.clickColor":
                Watching(() => Scan(step, PixelColor.Parse(ReadValue(step, "color").AsText()),
                    ReadValue(step, "tolerance").AsNumber(), measured: false));
                break;

            case "vision.findColors":
                Watching(() => PatternHits(step));
                break;

            case "vision.matchColor":
            case "vision.getPixel":
                Watching(() =>
                {
                    var (point, window) = Spot(step);
                    SawPixel(step, point, PixelAt(step, point, window).ToHex(), window);
                });
                break;

            case "vision.waitStable":
                // Whether something has stopped moving is only answered by time passing, so one look
                // shows what is on the screen and leaves the waiting to a run.
                Watching(() =>
                {
                    if (SearchAreas(step).FirstOrDefault() is { Frame: { } frame, Origin: { } origin })
                    {
                        Took(step, (frame, origin));
                    }
                });
                break;

            case "vision.capture":
                Watching(() => Took(step, CaptureBox(step)));
                break;

            case "vision.captureWindow":
                Watching(() => Took(step, WindowBox(step)));
                break;

            case "ocr.recognize":
                Watching(() => WasRead(step, ReadBox(step)));
                break;

            case "ocr.findText":
            case "ocr.clickText":
                Watching(() => SawSought(step));
                break;

            default:
                break;
        }
    }

    /// <summary>Hands over a picture that was taken, without keeping it anywhere.</summary>
    private void Took(ExecutableStep step, (ImageFrame Frame, ScreenPoint Corner) taken)
        => Saw(step, LookKind.Capture, taken.Frame, taken.Corner,
            [
                new(new ImageMatch(0, taken.Corner,
                    new ScreenSize(taken.Frame.Width, taken.Frame.Height)), LookRole.Area),
            ],
            // The name the picture would be handed over under, which is what the run itself puts
            // here when it really takes one; a step asked to try its looking writes nothing.
            1, step.Return("image"));

    private void Took(ExecutableStep step,
        (ImageFrame Frame, ScreenPoint Corner, WindowInfo Window) taken)
        => Took(step, (taken.Frame, taken.Corner));

    /// <summary>Hands over what was read off a picture, without writing the reading anywhere.</summary>
    private void WasRead(ExecutableStep step,
        (ScreenPoint Corner, ImageFrame Area, IReadOnlyList<TextSpan> Spans) read)
        => Saw(step, LookKind.Text, read.Area, read.Corner,
            Marks([(read.Area, read.Corner)], TextMarks(read.Spans, read.Corner), 0), 0,
            note: string.Join(' ', read.Spans.Select(span => span.Text)),
            minimum: Number(step, "minScore"));

    /// <summary>Hands over what a step looking for writing would find right now.</summary>
    private void SawSought(ExecutableStep step)
    {
        var wanted = ReadValue(step, "queryText").AsText();
        var way = TextWay(step, wanted);
        var readings = ReadSpans(step);
        var (_, chosen) = Counting(step, readings, wanted, way);

        SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen,
            wanted, minimum: Number(step, "minScore"));
    }
}
