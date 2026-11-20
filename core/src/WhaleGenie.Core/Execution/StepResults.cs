using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The result layer every family hands its work through: what a step offers, whether it asked for a
/// result, and the one shape a hit, a point and a list of them come in. Writing a result the step
/// did not ask for is not an error and not a variable: it is a name the step never makes.
/// </summary>
internal sealed class StepResults(VariableStore variables, StepProgress here, ScreenPoints points,
    Action<LogLevel, int, string, string, object[]> log)
{
    /// <summary>
    /// Hands one of a step's results over under the name the step gave it, and does nothing at all
    /// when the step did not ask for that result. An unticked result is not an empty variable, it is
    /// one this step never makes: a macro that reads it is reading a name no run wrote. What the step
    /// worked out is kept for the debugger either way, under the name the macro keeps it by when
    /// there is one, so a result nobody ticked can still be read after the run.
    /// </summary>
    public string Give(ExecutableStep step, string key, Value value)
    {
        if (ActionSemanticsCatalog.Find(step.Type) is not { } definition)
        {
            throw new StepFailure(RunKeys.Unsupported, step.Type);
        }

        var declared = definition.Return(key);
        var name = step.Return(key);
        if (declared is null)
        {
            if (name.Length > 0)
            {
                throw new StepFailure(RunKeys.BadResult, key);
            }

            // Shared helpers empty every result a search family may have. A key this particular
            // action does not offer was never a result of the step, so there is nothing to write.
            return string.Empty;
        }

        if (!StepChecks.ReturnFits(declared, value))
        {
            throw new StepFailure(RunKeys.VariableTypeMismatch, key);
        }

        if (name.Length > 0)
        {
            variables.Set(name, value);
        }

        if (here.Worked is { } worked)
        {
            var workedOut = new StepResult(key, name, value.AsText())
            {
                Data = Structured(value) ? JsonValues.Node(value) : null,
            };
            var at = worked.FindIndex(result =>
                string.Equals(result.Key, key, StringComparison.OrdinalIgnoreCase));

            // A step that was retried works the same result out again, and the last one is what it
            // came to; a result that has never been worked out before goes on the end.
            if (at < 0)
            {
                worked.Add(workedOut);
            }
            else
            {
                worked[at] = workedOut;
            }
        }

        return name;
    }

    /// <summary>
    /// True when a value is made of named fields rather than being one piece of data: a record, or a
    /// list or record that holds one somewhere inside it. What a step hands over is shown when it is
    /// this shape — the debugger puts one field on each line — where a list of plain numbers reads
    /// better as the line of text it already is.
    /// </summary>
    private static bool Structured(Value value) => value.Kind switch
    {
        ValueKind.Record => true,
        ValueKind.List => value.Items.Any(Structured),
        _ => false,
    };

    /// <summary>Hands one result over and says so in the log, the way storing a value does.</summary>
    public void Store(ExecutableStep step, string key, Value value, int depth)
    {
        var name = Give(step, key, value);
        if (name.Length > 0)
        {
            log(LogLevel.Info, depth, step.Type, LogKeys.Set, [name, value.AsText()]);
        }
    }

    /// <summary>True when the step keeps this result, so working it out is worth anything.</summary>
    public static bool Wants(ExecutableStep step, string key) => step.Return(key).Length > 0;

    /// <summary>
    /// Hands over what a search found as one record: where it was, the four sides of the rectangle
    /// it was found in, how sure the reading was, and the writing that was read where there was any.
    /// All of it about one hit, so it travels together under the names a macro reads with
    /// <c>field(hit, "x")</c> rather than as a variable of its own for each part.
    ///
    /// The rectangle is named by its top-left corner, because that is the corner a search region
    /// counts from: what a hit hands over has to be usable as where to look next without the macro
    /// working out an offset first. The middle is in the record beside it under names of its own,
    /// because that is the point a click or a move aims at.
    /// </summary>
    public void GiveMatch(ExecutableStep step, ScreenPoint corner, ScreenSize size, double score,
        string text = "", string label = "", int classId = -1)
    {
        Give(step, "match", HitRecord.Of(corner, size, score, text, label, classId));
        GivePoints(step, ScreenMath.Middle(corner, size), null);
        GiveTaken(step);
    }

    /// <summary>
    /// Hands over the point a step actually used in the spaces it knows. A point from a window
    /// action carries both; a foreground action with no window carries only the screen point.
    /// </summary>
    public void GivePoints(ExecutableStep step, ScreenPoint screen, ScreenPoint? window,
        string screenKey = "screenPoint", string windowKey = "windowPoint")
    {
        if (Wants(step, screenKey))
        {
            Give(step, screenKey, PointRecord(screen, "screen"));
        }

        if (!Wants(step, windowKey))
        {
            return;
        }

        if ((window ?? points.WindowPointAt(step, screen)) is { } client)
        {
            Give(step, windowKey, PointRecord(client, "window"));
        }
    }

    /// <summary>Hands over a screen point and, when the reading came from a window, its client point.</summary>
    public void GivePointAt(ExecutableStep step, ScreenPoint screen, WindowInfo? window)
    {
        if (!Wants(step, "screenPoint") && !Wants(step, "windowPoint"))
        {
            return;
        }

        if (!Wants(step, "windowPoint"))
        {
            GivePoints(step, screen, null);
            return;
        }

        var client = window is null
            ? points.WindowPointAt(step, screen)
            : points.InWindow(window, screen);
        GivePoints(step, screen, client);
    }

    /// <summary>One point as a record a later position field can read directly.</summary>
    private static Value PointRecord(ScreenPoint point, string basis)
        => Value.FromRecord(
        [
            (FieldNames.X, Value.FromNumber(point.X)),
            (FieldNames.Y, Value.FromNumber(point.Y)),
            (FieldNames.Basis, Value.FromText(basis)),
        ]);

    /// <summary>
    /// Hands over which window the picture came from and which way it was taken. That answers a
    /// different question from where the hit was: not "where is it", but "was it looking at the
    /// right thing at all".
    /// </summary>
    public void GiveTaken(ExecutableStep step)
    {
        Give(step, "window", Value.FromText(here.TakenWindow));
        Give(step, "method", Value.FromText(
            here.TakenMethod is { } method ? CaptureWayNames.Written(method) : string.Empty));
    }

    /// <summary>
    /// Empties what a search would have handed over, so a step inside a loop never reads the place
    /// the pass before it found. Nothing found is an empty hit and no hits at all, rather than the
    /// hit of the pass before: an empty list counts as nought, which is what "found nothing" means.
    /// </summary>
    public void GiveNothing(ExecutableStep step)
    {
        Give(step, "match", Value.FromText(string.Empty));
        Give(step, "every", Value.FromList([]));
        Give(step, "window", Value.FromText(string.Empty));
        Give(step, "method", Value.FromText(string.Empty));
        foreach (var name in new[]
                 {
                     "screenPoint", "windowPoint",
                     "screenStartPoint", "screenEndPoint",
                     "windowStartPoint", "windowEndPoint",
                 })
        {
            Give(step, name, Value.FromText(string.Empty));
        }
    }

    /// <summary>
    /// Hands over every hit there was, each a record shaped like the one that was taken, so a macro
    /// can count them, take one apart and loop over them all with the list functions. Only the steps
    /// that asked for them pay for the list.
    /// </summary>
    public void GiveEvery(ExecutableStep step, IEnumerable<Value> hits)
    {
        if (Wants(step, "every"))
        {
            Give(step, "every", Value.FromList(hits));
        }
    }
}
