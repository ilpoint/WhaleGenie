using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The text-recognition actions: reading the writing off the screen, finding a line of it, and
/// clicking the line that was found. Reading itself — the areas, the picture, the beat between two
/// looks — comes through <see cref="IScreenReads"/>, so a condition asking whether some text is
/// there reads the screen exactly the way these steps do.
/// </summary>
internal static class OcrActions
{
    /// <summary>
    /// The text actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["ocr.recognize"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => Recognize(run, step, depth, token));
                return Signal.Normal;
            },

            ["ocr.findText"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                run.Reads.Showing(() => FindText(run, step, depth, token));
                return Signal.Normal;
            },

            ["ocr.clickText"] = async (run, step, depth, token) =>
            {
                await run.Reads.WaitForScreen(token);
                await run.Reads.Showing(() => ClickText(run, step, depth, token));
                return Signal.Normal;
            },
        };

    private static void Recognize(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        // 搜索范围可能写下好几处，也可能一处都没写（那就是整块屏幕或窗口）。每一处都读，
        // 读到的按区域顺序接起来：同一段话分在两个框里，读出来的仍是一句话。
        var (spans, areas) = run.Reads.Read(step, token);
        var (frame, corner) = areas[0];

        run.Points.GivePoints(step,
            ScreenMath.Middle(corner, new ScreenSize(frame.Width, frame.Height)), null);
        var text = string.Join(' ', spans.Select(span => span.Text));
        run.Reads.SawSearch(step, LookKind.Text, areas, TextMarks.Of(spans, default), 0, note: text,
            minimum: run.Args(step).Number("minScore"));

        // Read as a table, the result holds rows, each one a record keyed by where its cells sit —
        // the shape reading a table through UI Automation gives. Read as text it holds the writing,
        // and either way the writing itself is handed over as well, so a macro can take it both
        // ways round.
        if (run.Flag(step, "table", false))
        {
            // A page read off the screen has no strip of column names, so the places its cells sit
            // in name them: A, B, C, the same names a file with no header gets.
            var rows = OcrReading.Rows(spans);
            run.Give(step, "table", Value.FromList(rows.Select(row => Value.FromRecord(
                row.Select((cell, index) => (TableColumns.Letter(index), Value.FromText(cell)))))));
            text = string.Join('\n', rows.Select(row => string.Join('\t', row)));
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadTable, rows.Count);
        }

        run.Store(step, "text", Value.FromText(text), depth);
    }

    private static void FindText(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var wanted = run.Args(step).ValueOf("queryText").AsText();
        var way = TextWay(run, step, wanted);

        // Every reading is kept, not only the ones the step is willing to act on: a look that
        // shows what the model made of the screen is what tells a step that found nothing whether
        // the writing was missing or the reading was.
        var (readings, areas) = run.Reads.Read(step, token);
        var (hits, chosen) = run.Reads.Counting(step, readings, wanted, way);

        run.Reads.SawSearch(step, LookKind.Text, areas, TextMarks.Of(readings, default), chosen, wanted,
            minimum: run.Args(step).Number("minScore"));

        var span = hits.Count > 0 ? hits[0] : null;
        run.Give(step, "found", Value.FromBool(span is not null));

        if (span is null)
        {
            run.GiveNothing(step);
        }
        else
        {
            run.GiveMatch(step, span.Location, span.Size, span.Confidence, span.Text);
        }

        // Every reading, not only the one the step picked, so a macro can walk a list of them or
        // click through a column of them; what each of them said is in its own record.
        run.GiveEvery(step, Hits(hits));

        run.Log(LogLevel.Info, depth, step.Type,
            span is null ? LogKeys.TextMissing : LogKeys.TextFound,
            span is null ? string.Empty : $"{span.Center.X},{span.Center.Y}",
            span is null ? string.Empty : span.Text);
    }

    /// <summary>
    /// Clicks the line the step is looking for, waiting for it to turn up. A step that asked to
    /// look again waits a moment and confirms the line is still there: a line that has already been
    /// replaced is not clicked.
    /// </summary>
    private static async Task ClickText(IRunContext run, ExecutableStep step, int depth,
        CancellationToken token)
    {
        var wanted = run.Args(step).ValueOf("queryText").AsText();
        var way = TextWay(run, step, wanted);
        var interval = run.Reads.Beat();

        // The readings of the last look are kept, so the look handed over at the end is of the
        // screen as it was when the step gave up or found what it wanted.
        IReadOnlyList<TextSpan> readings = [];
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> areas = [];
        List<TextSpan> hits = [];
        var chosen = 0;

        TextSpan? Look()
        {
            (readings, areas) = run.Reads.Read(step, token);
            (hits, chosen) = run.Reads.Counting(step, readings, wanted, way);
            return hits.Count > 0 ? hits[0] : null;
        }

        var span = await Pacing.WaitForValueAsync(Look, interval, token)
            ?? throw new StepFailure(RunKeys.TextNotFound, wanted);
        run.Reads.SawSearch(step, LookKind.Text, areas, TextMarks.Of(readings, default), chosen, wanted,
            minimum: run.Args(step).Number("minScore"));

        // The second look is a single look rather than another wait: whether the line is still
        // there is not a thing to wait for.
        var again = run.Pace(run.Args(step).Number("confirmMs"));
        if (again > 0)
        {
            await Pacing.Pause(again, token);
            span = Look() ?? throw new StepFailure(RunKeys.TextNotFound, wanted);
            run.Reads.SawSearch(step, LookKind.Text, areas, TextMarks.Of(readings, default), chosen, wanted,
                minimum: run.Args(step).Number("minScore"));
        }

        var at = run.Points.Scatter(step, new ScreenPoint(
            span.Center.X + run.Args(step).Number("offsetX"),
            span.Center.Y + run.Args(step).Number("offsetY")));

        // Where the line was read, every other place it was read, and what each of them said — the
        // same answers the step that only looks for text hands over.
        run.GiveMatch(step, span.Location, span.Size, span.Confidence, span.Text);
        run.GiveEvery(step, Hits(hits));

        run.Reads.ClickAt(step, at);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedText, wanted, $"{at.X},{at.Y}");
    }

    /// <summary>Every reading as a record, in the order the step counts them in.</summary>
    private static IEnumerable<Value> Hits(IReadOnlyList<TextSpan> hits)
        => hits.Select(hit => HitRecord.Of(hit.Location, hit.Size, hit.Confidence, hit.Text));

    /// <summary>
    /// The way a step looks for text, read and checked before anything is looked at: a word that is
    /// none of the five, or a pattern the machine cannot read, is refused here rather than after a
    /// screen has been read and compared.
    /// </summary>
    private static string TextWay(IRunContext run, ExecutableStep step, string wanted)
        => TextRule.Checked(run.Args(step).Text("matchMode"), wanted);
}
