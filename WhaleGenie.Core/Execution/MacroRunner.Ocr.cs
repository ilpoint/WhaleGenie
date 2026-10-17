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
    // ---------------------------------------------------------------------- ocr

    /// <summary>
    /// Reads the text in a step's search areas, with the positions in screen coordinates. Several
    /// areas are read one after another and what they hold is handed back together, and the areas
    /// themselves are kept so that a look at this reading can draw where it was taken from.
    /// </summary>
    private IReadOnlyList<TextSpan> ReadSpans(ExecutableStep step)
    {
        var areas = SearchAreas(step);
        _read = areas;

        var spans = new List<TextSpan>();
        foreach (var (area, origin) in areas)
        {
            spans.AddRange(ReadFrame(step, area).Select(span => span with
            {
                Location = new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y),
            }));
        }

        Sorted(spans, step);
        return spans;
    }

    /// <summary>
    /// Reads one picture, with whatever tidying up the step asked for, and hands the writing back
    /// in the coordinates of the picture that was given: one that was made bigger to be read has
    /// its positions made smaller again, so they still mean screen pixels.
    /// </summary>
    private IReadOnlyList<TextSpan> ReadFrame(ExecutableStep step, ImageFrame frame)
    {
        var (prepared, scale) = OcrPreprocess.Apply(Isolated(step, frame), ReadText(step, "preprocess"));
        var readings = _devices.Ocr.Recognize(prepared)
            .Select(span => span with { Text = Corrected(step, span.Text) });
        var spans = Wanted(step, [.. readings]);

        if (scale == 1)
        {
            return spans;
        }

        return [.. spans.Select(span => span with
        {
            Location = new ScreenPoint(Smaller(span.Location.X, scale), Smaller(span.Location.Y, scale)),
            Size = new ScreenSize(Smaller(span.Size.Width, scale), Smaller(span.Size.Height, scale)),
        })];
    }

    /// <summary>A measurement taken from a picture that was made bigger, put back to screen pixels.</summary>
    private static int Smaller(int value, double scale) => (int)Math.Round(value / scale);

    /// <summary>
    /// The picture with everything that is not the writing thrown away, when the step said what
    /// colour the writing is in. Writing drawn with an outline or over a picture is where the model
    /// is handed too much to read; leaving only the writing is what makes it readable.
    /// </summary>
    private ImageFrame Isolated(ExecutableStep step, ImageFrame frame)
    {
        var colour = ReadValue(step, "colorFilter").AsText().Trim();
        return colour.Length == 0
            ? frame
            : OcrPreprocess.ByColour(frame, PixelColor.Parse(colour), Number(step, "colorTolerance"));
    }

    /// <summary>
    /// The reading with the step's own corrections applied, written one to a line as what the model
    /// read = what it says. A model that keeps misreading a stylised font is a mistake the user can
    /// see and write down, and writing it down here is what keeps the macro working.
    /// </summary>
    private string Corrected(ExecutableStep step, string text)
    {
        var corrections = ReadText(step, "fixText");
        if (text.Length == 0 || corrections.Trim().Length == 0)
        {
            return text;
        }

        var fixedUp = text;
        foreach (var line in corrections.Split('\n'))
        {
            var pair = line.Split('=', 2);
            if (pair.Length != 2 || pair[0].Trim().Length == 0)
            {
                continue;
            }

            fixedUp = fixedUp.Replace(pair[0].Trim(), pair[1].Trim(), StringComparison.Ordinal);
        }

        return fixedUp;
    }

    /// <summary>
    /// The rectangle a reading step reads: where it starts on screen, the picture taken of it, and
    /// what was read off that picture.
    /// </summary>
    private (ScreenPoint Corner, ImageFrame Area, IReadOnlyList<TextSpan> Spans) ReadBox(
        ExecutableStep step)
    {
        var anchor = AnchorOf(step);
        var (corner, width, height) = Box(step, anchor.Corner);
        var shot = Shot(step, corner.X, corner.Y, width, height, anchor.Window);
        return (shot.Origin, shot.Frame, ReadFrame(step, shot.Frame));
    }

    /// <summary>
    /// The readings of a step's areas that it would act on, and which of them it would pick,
    /// counted from one inside the readings it was given.
    /// </summary>
    private (List<TextSpan> Hits, int Chosen) Counting(ExecutableStep step,
        IReadOnlyList<TextSpan> readings, string wanted, string mode)
    {
        var hits = new List<TextSpan>();
        var chosen = 0;
        for (var index = 0; index < readings.Count; index++)
        {
            if (!Sure(step, readings[index]) || !ActsOn(step, readings[index].Text, wanted, mode))
            {
                continue;
            }

            hits.Add(readings[index]);
            if (chosen == 0)
            {
                chosen = index + 1;
            }
        }

        return (hits, chosen);
    }

    /// <summary>
    /// Whether a reading is one the step would act on: it says what was asked for, and it has the
    /// shape the step asked for. The shape is what narrows a screen full of the same word down to
    /// the one line a macro wants — a level, an amount, a count — where looking for the word alone
    /// would take the first line that happens to say it.
    /// </summary>
    private bool ActsOn(ExecutableStep step, string text, string wanted, string mode)
    {
        if (!Matches(step, text, wanted, mode))
        {
            return false;
        }

        var shape = ReadText(step, "expected").Trim();
        if (shape.Length == 0)
        {
            return true;
        }

        try
        {
            return RegexGuard.IsMatch(text, shape, OptionsOf(step, TextCompare.Regex).Regex);
        }
        catch (ArgumentException)
        {
            // A pattern the machine cannot read is the macro's problem to see, not a stack trace to
            // puzzle over, so it comes back as a failed step.
            throw new StepFailure(RunKeys.BadPattern, shape);
        }
    }

    /// <summary>
    /// What a step keeps of what was read: the writing as it stands, one kind of character out of
    /// it, or what a pattern picks out. Cutting a screen down to the numbers, or to the letters, is
    /// how a screen full of labels and a screen full of codes are handled by the same kind of step.
    /// </summary>
    private IReadOnlyList<TextSpan> Wanted(ExecutableStep step, IReadOnlyList<TextSpan> spans)
        => ReadText(step, "content").Trim().ToLowerInvariant() switch
        {
            "digits" => OcrReading.Numbers(spans),
            "letters" => OcrReading.Letters(spans),
            "chinese" => OcrReading.Chinese(spans),
            "lettersandchinese" => OcrReading.LettersAndChinese(spans),
            "regex" => Extracted(step, spans),
            _ => spans,
        };

    /// <summary>
    /// The pieces the step's own pattern picks out of what was read. A pattern with nothing in it
    /// asks for no cutting, which is what a step that chose the pattern and then left it empty is
    /// saying; a pattern the machine cannot read comes back as a failed step rather than as a look
    /// that found nothing.
    /// </summary>
    private IReadOnlyList<TextSpan> Extracted(ExecutableStep step, IReadOnlyList<TextSpan> spans)
    {
        var pattern = ReadValue(step, "contentPattern").AsText().Trim();
        if (pattern.Length == 0)
        {
            return spans;
        }

        try
        {
            return OcrReading.ByPattern(spans, RegexGuard.Create(pattern));
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, pattern);
        }
    }

    private void Recognize(ExecutableStep step, int depth)
    {
        var (corner, area, spans) = ReadBox(step);
        var text = string.Join(' ', spans.Select(span => span.Text));
        Saw(step, LookKind.Text, area, corner, Marks([(area, corner)], TextMarks(spans, corner), 0), 0,
            note: text, minimum: Number(step, "minScore"));

        // Read as a table, the result holds rows, each one a record keyed by where its cells sit —
        // the shape reading a table through UI Automation gives. Read as text it holds the writing,
        // and either way the writing itself is handed over as well, so a macro can take it both
        // ways round.
        if (Flag(step, "table", false))
        {
            // A page read off the screen has no strip of column names, so the places its cells sit
            // in name them: A, B, C, the same names a file with no header gets.
            var rows = OcrReading.Rows(spans);
            Give(step, "table", Value.FromList(rows.Select(row => Value.FromRecord(
                row.Select((cell, index) => (Letter(index), Value.FromText(cell)))))));
            text = string.Join('\n', rows.Select(row => string.Join('\t', row)));
            Log(LogLevel.Info, depth, step.Type, LogKeys.ReadTable, rows.Count);
        }

        Store(step, "text", Value.FromText(text), depth);
    }

    private void FindText(ExecutableStep step, int depth)
    {
        var wanted = ReadValue(step, "queryText").AsText();
        var way = TextWay(step, wanted);

        // Every reading is kept, not only the ones the step is willing to act on: a look that
        // shows what the model made of the screen is what tells a step that found nothing whether
        // the writing was missing or the reading was.
        var readings = ReadSpans(step);
        var (hits, chosen) = Counting(step, readings, wanted, way);

        SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen, wanted,
            minimum: Number(step, "minScore"));

        var span = hits.Count > 0 ? hits[0] : null;

        if (span is null)
        {
            GiveNothing(step);
        }
        else
        {
            GiveMatch(step, span.Location, span.Size, span.Confidence, span.Text);
        }

        // Every reading, not only the one the step picked, so a macro can walk a list of them or
        // click through a column of them; what each of them said is in its own record.
        GiveEvery(step, HitRecords(hits));

        Log(LogLevel.Info, depth, step.Type, span is null ? LogKeys.TextMissing : LogKeys.TextFound,
            span is null ? string.Empty : $"{span.Center.X},{span.Center.Y}",
            span is null ? string.Empty : span.Text);
    }

    /// <summary>What one reading of the screen reads as a mark: the writing, with its own score.</summary>
    private static List<(ImageMatch Match, string? Label)> TextMarks(
        IReadOnlyList<TextSpan> spans, ScreenPoint origin)
        => [.. spans.Select(span => (
            new ImageMatch(span.Confidence,
                new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y),
                span.Size),
            (string?)span.Text))];

    private bool Sure(ExecutableStep step, TextSpan candidate)
        => candidate.Confidence >= Number(step, "minScore");

    private async Task ClickText(ExecutableStep step, int depth, CancellationToken token)
    {
        var wanted = ReadValue(step, "queryText").AsText();
        var way = TextWay(step, wanted);
        var interval = LookBeat(step);

        // The readings of the last look are kept, so the look handed over at the end is of the
        // screen as it was when the step gave up or found what it wanted.
        IReadOnlyList<TextSpan> readings = [];
        List<TextSpan> hits = [];
        var chosen = 0;

        TextSpan? Look()
        {
            readings = ReadSpans(step);
            (hits, chosen) = Counting(step, readings, wanted, way);
            return hits.Count > 0 ? hits[0] : null;
        }

        var span = await WaitForValueAsync(Look, interval, token);
        SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen, wanted,
            minimum: Number(step, "minScore"));

        // A step that asked to look again waits a moment and confirms the line is still there: a
        // line that has already been replaced is not clicked. The second look is a single look
        // rather than another wait: whether the line is still there is not a thing to wait for.
        var again = Pace(OptionalNumber(step, "recheckMs", 0));
        if (again > 0)
        {
            await Pause(again, token);
            span = Look() ?? throw new StepFailure(RunKeys.TextNotFound, wanted);
            SawSearch(step, LookKind.Text, _read, TextMarks(readings, default), chosen, wanted,
                minimum: Number(step, "minScore"));
        }

        var at = Scattered(new ScreenPoint(
            span.Center.X + Number(step, "offsetX"),
            span.Center.Y + Number(step, "offsetY")), Spread(step));

        // Where the line was read, every other place it was read, and what each of them said — the
        // same answers the step that only looks for text hands over.
        GiveMatch(step, span.Location, span.Size, span.Confidence, span.Text);
        GiveEvery(step, HitRecords(hits));

        await ClickAt(step, at, token);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClickedText, wanted, $"{at.X},{at.Y}");
    }

    /// <summary>
    /// Compares found text against what was asked for, the way the step chose. The five ways
    /// themselves live in one place (<see cref="TextCompare"/>); what is added here is what each
    /// kind of mistake means: a word that is none of the five, or a pattern the machine cannot read,
    /// comes back as a failed step rather than as a stack trace or, worse, as some other way of
    /// comparing than the one that was written.
    /// </summary>
    private bool Matches(ExecutableStep step, string found, string wanted, string mode)
    {
        var way = Way(mode);
        try
        {
            return TextCompare.Matches(found, wanted, way, OptionsOf(step, way));
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, wanted);
        }
    }

    /// <summary>
    /// One of the five ways of comparing text, as the step wrote it. An unanswered field reads as
    /// "contained in it"; anything else the five do not name is worth saying out loud, because
    /// quietly comparing in some other way would turn "exactly this" into "this is part of it".
    /// </summary>
    private static string Way(string written)
    {
        try
        {
            return TextCompare.Read(written);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadMatchMode, written.Trim());
        }
    }

    /// <summary>
    /// The switches a text comparison carries. Spacing is a question only "exactly" asks by
    /// default; leaving the switch out keeps that default, while writing it false keeps the spaces
    /// the text actually has.
    /// </summary>
    private TextCompareOptions OptionsOf(ExecutableStep step, string way)
        => new(
            Flag(step, "caseSensitive", false),
            way == TextCompare.Exactly ? Flag(step, "trim", true) : null,
            Flag(step, "multiline", false));

}
