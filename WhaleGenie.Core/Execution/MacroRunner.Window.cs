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

public sealed partial class MacroRunner
{
    // --------------------------------------------------------------------- window

    /// <summary>
    /// Hands one of a step's results over under the name the step gave it, and does nothing at all
    /// when the step did not ask for that result. An unticked result is not an empty variable, it is
    /// one this step never makes: a macro that reads it is reading a name no run wrote. What the step
    /// worked out is kept for the debugger either way, under the name the macro keeps it by when
    /// there is one, so a result nobody ticked can still be read after the run.
    /// </summary>
    private string Give(ExecutableStep step, string key, Value value)
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

        if (!ReturnFits(declared, value))
        {
            throw new StepFailure(RunKeys.BadResultType, key);
        }

        if (name.Length > 0)
        {
            Variables.Set(name, value);
        }

        if (_worked is { } worked)
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
    private void Store(ExecutableStep step, string key, Value value, int depth)
    {
        var name = Give(step, key, value);
        if (name.Length > 0)
        {
            Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, value.AsText());
        }
    }

    /// <summary>True when the step keeps this result, so working it out is worth anything.</summary>
    private static bool Wants(ExecutableStep step, string key) => step.Return(key).Length > 0;

    /// <summary>
    /// Hands over what a search found as one record: where it was, the four sides of the rectangle
    /// it was found in, how sure the reading was, and the writing that was read where there was any.
    /// All of it about one hit, so it travels together under the names a macro reads with
    /// <c>field($hit, "x")</c> rather than as a variable of its own for each part.
    ///
    /// The rectangle is named by its top-left corner, because that is the corner a search region
    /// counts from: what a hit hands over has to be usable as where to look next without the macro
    /// working out an offset first. The middle is in the record beside it under names of its own,
    /// because that is the point a click or a move aims at.
    /// </summary>
    private void GiveMatch(ExecutableStep step, ScreenPoint corner, ScreenSize size, double score,
        string text = "", string label = "", int classId = -1)
    {
        Give(step, "match", Hit(corner, size, score, text, label, classId));
        GivePoints(step, Middle(corner, size), null);
        GiveTaken(step);
    }

    /// <summary>
    /// Hands over which window the picture came from and which way it was taken. That answers a
    /// different question from where the hit was: not "where is it", but "was it looking at the
    /// right thing at all".
    /// </summary>
    private void GiveTaken(ExecutableStep step)
    {
        Give(step, "window", Value.FromText(_takenWindow));
        Give(step, "method", Value.FromText(
            _takenMethod is { } method ? CaptureWayNames.Written(method) : string.Empty));
    }

    /// <summary>
    /// One hit as a record. Every hit carries the same fields, whatever was found: the same
    /// rectangle, the same score and the same place to put a kind and a piece of writing, empty
    /// where the action read none. A macro that reads the size of a picture and one that reads the
    /// size of a line of writing therefore spell it the same way.
    /// </summary>
    private static Value Hit(ScreenPoint corner, ScreenSize size, double score, string text = "",
        string label = "", int classId = -1)
    {
        var centre = Middle(corner, size);
        return Value.FromRecord(
        [
            (FieldNames.X, Value.FromNumber(centre.X)),
            (FieldNames.Y, Value.FromNumber(centre.Y)),
            (FieldNames.Left, Value.FromNumber(corner.X)),
            (FieldNames.Top, Value.FromNumber(corner.Y)),
            (FieldNames.Width, Value.FromNumber(size.Width)),
            (FieldNames.Height, Value.FromNumber(size.Height)),
            (FieldNames.Score, Value.FromNumber(score)),
            (FieldNames.Box, Value.FromText($"{corner.X},{corner.Y},{size.Width},{size.Height}")),
            (FieldNames.Text, Value.FromText(text)),
            (FieldNames.Class, Value.FromNumber(classId)),
            (FieldNames.Label, Value.FromText(label)),
        ]);
    }

    /// <summary>The middle of a rectangle, which is the point an action aims at.</summary>
    private static ScreenPoint Middle(ScreenPoint corner, ScreenSize size)
        => new(corner.X + (size.Width / 2), corner.Y + (size.Height / 2));

    /// <summary>
    /// Empties what a search would have handed over, so a step inside a loop never reads the place
    /// the pass before it found. Nothing found is an empty hit and no hits at all, rather than the
    /// hit of the pass before: an empty list counts as nought, which is what "found nothing" means.
    /// </summary>
    private void GiveNothing(ExecutableStep step)
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
    private void GiveEvery(ExecutableStep step, IEnumerable<Value> hits)
    {
        if (Wants(step, "every"))
        {
            Give(step, "every", Value.FromList(hits));
        }
    }

    /// <summary>Every picture hit as a record, in the order the step counts them in.</summary>
    private static IEnumerable<Value> HitRecords(IReadOnlyList<ImageMatch> hits)
        => hits.Select(hit => Hit(hit.Location, hit.Size, hit.Score));

    /// <summary>Every thing a detection model found as a record, kind of thing and all.</summary>
    private static IEnumerable<Value> HitRecords(IReadOnlyList<Sighting> hits)
        => hits.Select(hit => Hit(hit.Match.Location, hit.Match.Size, hit.Match.Score,
            label: hit.Label, classId: hit.ClassId));

    /// <summary>Every reading of the screen as a record, keeping what each of them said.</summary>
    private static IEnumerable<Value> HitRecords(IReadOnlyList<TextSpan> hits)
        => hits.Select(hit => Hit(hit.Location, hit.Size, hit.Confidence, hit.Text));

    /// <summary>Leaves true or false behind, depending on whether the window is open.</summary>
    private void WindowExists(ExecutableStep step, int depth)
    {
        var found = Lookup(step) is not null;
        Store(step, "found", Value.FromBool(found), depth);
    }

    /// <summary>Waits until a window the step names appears.</summary>
    private async Task WaitForWindow(ExecutableStep step, int depth, CancellationToken token)
    {
        var timeout = step.Meta.TimeoutMs;

        var window = await WaitForValueAsync(() => Lookup(step), timeout, 100, token);
        if (window is null)
        {
            throw new StepFailure(RunKeys.WindowTimeout, WindowText(step));
        }

        Give(step, "title", Value.FromText(window.Title));

        if (Wants(step, "window"))
        {
            // The whole record costs a look at the program, the class and the client area; a step
            // that only wants the title should not pay for them.
            var client = _devices.Windows.ClientOrigin(window.Handle);
            var inner = _devices.Windows.ClientSize(window.Handle);
            Give(step, "window", WindowRecord(window,
                _devices.Windows.ProcessOf(window.Handle),
                _devices.Windows.ClassOf(window.Handle),
                $"{client.X},{client.Y},{inner.Width},{inner.Height}",
                WindowRuleOf(step)));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.WindowAppeared, window.Title);
    }

    /// <summary>
    /// Collects the open windows into a list, narrowed down to the ones a step names, each window as
    /// a record of its own: its title, where it sits, what the system knows it by, and — when the
    /// step asks for them — the program and the class behind it. Two windows with the same title are
    /// two windows, so each gets a record of its own. A text nobody wrote means every window here,
    /// where it means the window in front to a step that acts on one of them.
    /// </summary>
    private void ListWindows(ExecutableStep step, int depth)
    {
        var windows = Matching(step);
        var rule = WindowRuleOf(step);

        // The program and the class of a window are read only when the step keeps the list and asks
        // for them, because looking either of them up costs a look at the window. Every entry is
        // then the whole of one window, the same record window.info hands over for a single one; a
        // step that leaves them out gets two empty fields rather than missing ones.
        if (Wants(step, "windows"))
        {
            var details = Flag(step, "details", false);
            Store(step, "windows", Value.FromList(windows.Select(window => WindowRecord(window,
                details ? _devices.Windows.ProcessOf(window.Handle) : string.Empty,
                details ? _devices.Windows.ClassOf(window.Handle) : string.Empty,
                string.Empty, rule))), depth);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.ListedWindows, windows.Count);
    }

    /// <summary>
    /// The windows a listing hands over, in the order the system gives them: the ones the step's
    /// text picks out, or every window there is when it names none.
    /// </summary>
    private IReadOnlyList<WindowInfo> Matching(ExecutableStep step)
    {
        var rule = WindowRuleOf(step);
        return _devices.Windows.Filter(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
            everyWhenEmpty: true, options: rule.Options);
    }

    /// <summary>Does something to the window a step names, or fails the step when it is not open.</summary>
    private void Act(ExecutableStep step, int depth, string message, Func<WindowInfo, bool> action)
    {
        var window = Locate(step);

        if (!action(window))
        {
            throw new StepFailure(RunKeys.WindowFailed, window.Title);
        }

        Log(LogLevel.Info, depth, step.Type, message, window.Title);
    }

    /// <summary>
    /// Where <c>window.move</c> sends a window: the rectangle a record says, or the sides the step
    /// wrote, with the ones it left empty staying where they are, which is what its empty cells
    /// mean. A window is never told to be zero pixels wide.
    /// </summary>
    private (int X, int Y, int Width, int Height) MoveTarget(ExecutableStep step, WindowInfo window)
    {
        if (RecordedRect(step, "rect") is { } rect)
        {
            return (rect.Left, rect.Top, rect.Width, rect.Height);
        }

        int Side(string name, int current, int least)
            => Given(step, name) ? Math.Max(least, Number(step, name)) : current;

        return (Side("x", window.Location.X, int.MinValue),
            Side("y", window.Location.Y, int.MinValue),
            Side(FieldNames.Width, window.Size.Width, 1),
            Side(FieldNames.Height, window.Size.Height, 1));
    }

    /// <summary>Whether the step says enough about a window's rectangle to move it at all.</summary>
    private static bool MoveTargetWritten(ExecutableStep step)
        => step.Text("rect").Trim().Length > 0
           || new[] { "x", "y", FieldNames.Width, FieldNames.Height }
               .Any(name => step.Text(name).Trim().Length > 0);

    /// <summary>
    /// The window a step names, or a failure saying which one could not be found. "Match by" says
    /// what the text is held up against and how; an empty text means whatever window is in front.
    /// </summary>
    private WindowInfo Locate(ExecutableStep step)
        => Lookup(step) ?? throw new StepFailure(RunKeys.WindowNotFound, WindowText(step));

    /// <summary>The same, for the steps that report rather than fail when no window matches.</summary>
    private WindowInfo? Lookup(ExecutableStep step)
    {
        var rule = WindowRuleOf(step);
        if (!rule.HasTarget)
        {
            return _devices.Windows.Foreground();
        }

        return _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
            rule.Options);
    }

    /// <summary>
    /// The one window rule every step family reads. A literal rule is written as a record and a
    /// variable may hand over a record read earlier; no older title-only shape is read.
    /// </summary>
    private WindowRule WindowRuleOf(ExecutableStep step, string field = "window")
    {
        var written = step.Text(field).Trim();
        if (written.Length == 0)
        {
            return WindowRule.Empty;
        }

        var value = WindowValue(step, field);
        WindowRule rule;
        if (value.IsRecord)
        {
            rule = RuleFromRecord(value);
        }
        else if (WindowRule.TryParse(written, out var parsed))
        {
            rule = parsed;
        }
        else
        {
            throw new StepFailure(RunKeys.BadWindowRule, written);
        }

        var (match, compare) = rule.Reading;
        var selected = rule.MatchValue.Trim();

        if (match is WindowMatch.Handle && rule.Handle == 0)
        {
            throw new StepFailure(RunKeys.BadWindowRule,
                "a window handle rule needs a non-zero handle");
        }

        // A pattern the machine cannot read is the macro's problem to see, not a stack trace to
        // puzzle over, so it comes back as a failed step before anything is looked at — the same
        // words whichever step asked.
        try
        {
            WindowHow.Check(selected, compare, rule.Options);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, selected);
        }

        return rule with { How = string.IsNullOrWhiteSpace(rule.How) ? WindowHow.TitleContains : rule.How };
    }

    /// <summary>A window record read by a step, with the rule fields a match needs.</summary>
    private static WindowRule RuleFromRecord(Value record)
    {
        string Text(string name, string fallback = "")
            => record.Field(name)?.AsText().Trim() ?? fallback;

        bool Flag(string name, bool fallback)
            => record.Field(name)?.AsBool() ?? fallback;

        var how = Text(FieldNames.How, WindowHow.TitleContains);
        var selected = WindowHow.Read(how).Match switch
        {
            WindowMatch.Process => Text(FieldNames.Process),
            WindowMatch.ClassName => Text(FieldNames.Class),
            WindowMatch.Handle => record.Field(FieldNames.Handle)?.AsNumber() is { } windowHandle
                && windowHandle != 0
                ? windowHandle.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            _ => Text(FieldNames.Title),
        };
        if (selected.Length == 0)
        {
            var needed = WindowHow.Read(how).Match switch
            {
                WindowMatch.Process => FieldNames.Process,
                WindowMatch.ClassName => FieldNames.Class,
                WindowMatch.Handle => FieldNames.Handle,
                _ => FieldNames.Title,
            };
            throw new StepFailure(RunKeys.WindowRecordField, needed);
        }

        return new WindowRule(
            Text(FieldNames.Title),
            Text(FieldNames.Process),
            Text(FieldNames.Class),
            record.Field(FieldNames.Handle)?.AsNumber() is { } handle ? (long)handle : 0,
            how,
            Flag(FieldNames.CaseSensitive, false),
            Flag(FieldNames.Trim, true),
            Flag(FieldNames.Multiline, false));
    }

    /// <summary>
    /// What a step wrote into a window field: the value of the variable it names when that variable
    /// holds a window record, and the literal text otherwise. A cell that is one known name is read
    /// as that value so a record can be handed over whole.
    /// </summary>
    private Value WindowValue(ExecutableStep step, string field)
    {
        var written = step.Text(field).Trim();
        if (written.Length > 1 && written[0] == '$' && WindowName(written[1..])
            && Variables.TryGet(written[1..], out var value))
        {
            return value;
        }

        return Value.FromText(ReadText(step, field));
    }

    /// <summary>True when the text is one variable name and nothing else.</summary>
    private static bool WindowName(string name) => VariableText.IsName(name);

    /// <summary>What a step wrote into its window field, with the variables in it resolved.</summary>
    private string WindowText(ExecutableStep step) => WindowRuleOf(step).MatchValue.Trim();

    /// <summary>
    /// Reads where a window is and how big it is. The rectangle is written the way a search region
    /// is spelled, so it can be handed straight to the vision actions, and each part gets a name of
    /// its own for the arithmetic a macro wants to do with it.
    /// </summary>
    /// <summary>
    /// Where a window sits and what it is, handed over as one record — the same shape a listing
    /// gives each window it finds, so the rectangle goes into a search region as it stands and one
    /// part of it is read by name.
    /// </summary>
    private void WindowBox(ExecutableStep step, int depth)
    {
        var window = Locate(step);
        var box = $"{window.Location.X},{window.Location.Y},{window.Size.Width},{window.Size.Height}";

        if (Wants(step, "window"))
        {
            // The program, the class and the part inside the border cost a look at the window each,
            // so they are worked out only when the record is kept at all.
            var client = _devices.Windows.ClientOrigin(window.Handle);
            var inner = _devices.Windows.ClientSize(window.Handle);
            Give(step, "window", WindowRecord(window,
                _devices.Windows.ProcessOf(window.Handle),
                _devices.Windows.ClassOf(window.Handle),
                $"{client.X},{client.Y},{inner.Width},{inner.Height}",
                WindowRuleOf(step)));
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.WindowBox, window.Title, box);
    }

    /// <summary>
    /// One window as a record: what it is — the title, the program and the class behind it, what the
    /// system knows it by, whether it is shrunk or spread — and where it sits, both as the four
    /// numbers of its rectangle, as the middle of that rectangle under x and y, and as the text a
    /// search region is spelled with. The part inside the border is always there as a field; a
    /// listing leaves it empty, because reading it costs a look at every window found.
    /// </summary>
    private static Value WindowRecord(WindowInfo window, string program, string kind, string clientBox,
        WindowRule rule)
    {
        var left = window.Location.X;
        var top = window.Location.Y;
        var width = window.Size.Width;
        var height = window.Size.Height;
        var middle = Middle(window.Location, window.Size);

        return Value.FromRecord(
        [
            (FieldNames.Title, Value.FromText(window.Title)),
            (FieldNames.Process, Value.FromText(program)),
            (FieldNames.Class, Value.FromText(kind)),
            (FieldNames.Handle, Value.FromNumber(window.Handle)),
            (FieldNames.Minimized, Value.FromBool(window.Minimized)),
            (FieldNames.Maximized, Value.FromBool(window.Maximized)),
            (FieldNames.X, Value.FromNumber(middle.X)),
            (FieldNames.Y, Value.FromNumber(middle.Y)),
            (FieldNames.Left, Value.FromNumber(left)),
            (FieldNames.Top, Value.FromNumber(top)),
            (FieldNames.Width, Value.FromNumber(width)),
            (FieldNames.Height, Value.FromNumber(height)),
            (FieldNames.Box, Value.FromText($"{left},{top},{width},{height}")),
            (FieldNames.ClientBox, Value.FromText(clientBox)),
            (FieldNames.How, Value.FromText(rule.How)),
            (FieldNames.CaseSensitive, Value.FromBool(rule.CaseSensitive)),
            (FieldNames.Trim, Value.FromBool(rule.Trim)),
            (FieldNames.Multiline, Value.FromBool(rule.Multiline)),
        ]);
    }


    /// <summary>
    /// The window actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchWindow(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "window.exists":
                WindowExists(step, depth);
                return Signal.Normal;
            case "window.waitFor":
                await WaitForWindow(step, depth, token);
                return Signal.Normal;
            case "window.activate":
                Act(step, depth, LogKeys.ActivatedWindow, window => _devices.Windows.Activate(window.Handle));
                return Signal.Normal;
            case "window.minimize":
                Act(step, depth, LogKeys.MinimizedWindow, window => _devices.Windows.Minimize(window.Handle));
                return Signal.Normal;
            case "window.maximize":
                Act(step, depth, LogKeys.MaximizedWindow, window => _devices.Windows.Maximize(window.Handle));
                return Signal.Normal;
            case "window.restore":
                Act(step, depth, LogKeys.RestoredWindow, window => _devices.Windows.Restore(window.Handle));
                return Signal.Normal;
            case "window.move":
                if (MoveTargetWritten(step))
                {
                    Act(step, depth, LogKeys.MovedWindow, window =>
                    {
                        var (x, y, width, height) = MoveTarget(step, window);
                        return _devices.Windows.Move(window.Handle, x, y, width, height);
                    });
                }

                return Signal.Normal;
            case "window.close":
                Act(step, depth, LogKeys.ClosedWindow, window => _devices.Windows.Close(window.Handle));
                return Signal.Normal;
            case "window.list":
                ListWindows(step, depth);
                return Signal.Normal;
            case "window.info":
                WindowBox(step, depth);
                return Signal.Normal;

            // ------------------------------------------------------------------ input
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
