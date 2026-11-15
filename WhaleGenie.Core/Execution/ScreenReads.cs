using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Reading writing off the screen: the areas a step reads, what each picture came back as, the
/// corrections a step wrote down, and which readings it would act on. A condition asking whether
/// some text is there reads through the same code, so "is it on screen" and "find it" agree on what
/// counts as found.
/// </summary>
internal sealed class ScreenReads(IDeviceLayer devices, VariableStore variables, RunState run,
    StepProgress here, ScreenSight sight)
{
    /// <summary>The reader for the fields of one step.</summary>
    private StepArguments Args(ExecutableStep step)
        => new(step, variables, devices.Screen.RefreshMilliseconds);

    /// <summary>A yes/no field, with the answer a blank cell means.</summary>
    private static bool Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Hands over what was read off a picture, without writing the reading anywhere. The trial
    /// window's own look: what the model made of the screen, marked where it read it.
    /// </summary>
    public void WasRead(ExecutableStep step,
        (ScreenPoint Corner, ImageFrame Area, IReadOnlyList<TextSpan> Spans) read)
        => sight.Saw(step, LookKind.Text, read.Area, read.Corner,
            ScreenSight.Marks([(read.Area, read.Corner)], TextMarks.Of(read.Spans, read.Corner), 0), 0,
            note: string.Join(' ', read.Spans.Select(span => span.Text)),
            minimum: Args(step).Number("minScore"));

    /// <summary>Hands over what a step looking for writing would find right now.</summary>
    public void SawSought(ExecutableStep step)
    {
        var wanted = Args(step).ValueOf("queryText").AsText();
        var way = TextRule.Checked(Args(step).Text("matchMode"), wanted);
        var readings = ReadSpans(step);
        var (_, chosen) = Counting(step, readings, wanted, way);

        sight.SawSearch(step, LookKind.Text, here.Read, TextMarks.Of(readings, default), chosen,
            wanted, minimum: Args(step).Number("minScore"));
    }

    /// <summary>
    /// Reads the text in a step's search areas, with the positions in screen coordinates. Several
    /// areas are read one after another and what they hold is handed back together, and the areas
    /// themselves are kept so that a look at this reading can draw where it was taken from.
    /// </summary>
    public IReadOnlyList<TextSpan> ReadSpans(ExecutableStep step,
        CancellationToken token = default)
        => ReadBoth(step, token).Spans;

    /// <summary>
    /// The same reading, with the areas it was taken from: a look has to show where it looked, and
    /// reading them again for that would take a second picture of the same screen.
    /// </summary>
    public (IReadOnlyList<TextSpan> Spans,
        IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> Areas) ReadBoth(ExecutableStep step,
            CancellationToken token = default)
    {
        var areas = sight.SearchAreas(step);
        here.Read = areas;

        var spans = new List<TextSpan>();
        foreach (var (area, origin) in areas)
        {
            spans.AddRange(ReadFrame(step, area, token).Select(span => span with
            {
                Location = new ScreenPoint(span.Location.X + origin.X, span.Location.Y + origin.Y),
            }));
        }

        SearchHits.Sort(spans, step);
        return (spans, areas);
    }

    /// <summary>
    /// Reads one picture, with whatever tidying up the step asked for, and hands the writing back
    /// in the coordinates of the picture that was given: one that was made bigger to be read has
    /// its positions made smaller again, so they still mean screen pixels.
    /// </summary>
    private IReadOnlyList<TextSpan> ReadFrame(ExecutableStep step, ImageFrame frame,
        CancellationToken token = default)
    {
        // 识别一次整幅画面是同步的，读不到取消令牌：进识别之前先问一次，让「停止」不必等它。
        if (token.CanBeCanceled)
        {
            token.ThrowIfCancellationRequested();
        }
        else
        {
            run.RunToken.ThrowIfCancellationRequested();
        }

        var (prepared, scale) = OcrPreprocess.Apply(Isolated(step, frame), Args(step).Text("preprocess"));
        var readings = devices.Ocr.Recognize(prepared)
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
        var colour = Args(step).ValueOf("colorFilter").AsText().Trim();
        return colour.Length == 0
            ? frame
            : OcrPreprocess.ByColour(frame, PixelColor.Parse(colour), Args(step).Number("colorTolerance"));
    }

    /// <summary>
    /// The reading with the step's own corrections applied, written one to a line as what the model
    /// read = what it says. A model that keeps misreading a stylised font is a mistake the user can
    /// see and write down, and writing it down here is what keeps the macro working.
    /// </summary>
    private string Corrected(ExecutableStep step, string text)
    {
        var corrections = Args(step).Text("fixText");
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
    public (ScreenPoint Corner, ImageFrame Area, IReadOnlyList<TextSpan> Spans) ReadBox(
        ExecutableStep step)
    {
        // 搜索范围可能有好几处，试看只摆得出其中一张：摆第一处，与运行时从头读起的那一处一致。
        var (frame, origin) = sight.SearchAreas(step)[0];
        return (origin, frame, ReadFrame(step, frame));
    }

    /// <summary>
    /// The readings of a step's areas that it would act on, and which of them it would pick,
    /// counted from one inside the readings it was given.
    /// </summary>
    public (List<TextSpan> Hits, int Chosen) Counting(ExecutableStep step,
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

        var shape = Args(step).Text("expected").Trim();
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
        => Args(step).Text("content").Trim().ToLowerInvariant() switch
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
        var pattern = Args(step).ValueOf("contentPattern").AsText().Trim();
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

    private bool Sure(ExecutableStep step, TextSpan candidate)
        => candidate.Confidence >= Args(step).Number("minScore");

    /// <summary>
    /// Compares found text against what was asked for, the way the step chose. The five ways
    /// themselves live in one place (<see cref="TextCompare"/>); what is added here is what each
    /// kind of mistake means: a word that is none of the five, or a pattern the machine cannot read,
    /// comes back as a failed step rather than as a stack trace or, worse, as some other way of
    /// comparing than the one that was written.
    /// </summary>
    private bool Matches(ExecutableStep step, string found, string wanted, string mode)
    {
        var way = TextRule.Way(mode);
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
    /// The switches a text comparison carries. Spacing is a question only "exactly" asks by
    /// default; leaving the switch out keeps that default, while writing it false keeps the spaces
    /// the text actually has.
    /// </summary>
    private TextCompareOptions OptionsOf(ExecutableStep step, string way)
        => TextRule.Options(way, Flag(step, "caseSensitive", false), Flag(step, "trim", true),
            Flag(step, "multiline", false));
}
