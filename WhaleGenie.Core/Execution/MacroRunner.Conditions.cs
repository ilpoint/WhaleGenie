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

// The conditions a step can test: values, lists, windows, ranges, dates, text and colours.
public sealed partial class MacroRunner
{
    private bool Check(ExecutableStep step, string name, CancellationToken token)
    {
        RequireCondition(step, name);
        ValidateAnswers(step);
        token.ThrowIfCancellationRequested();

        // A condition written with a comparison sign was meant as an expression. Text that cannot
        // be worked out is otherwise used as the words it is, and words are true — so "1 = 2", the
        // usual typo for "==", quietly ran the branch it was written to skip. Anything shaped like
        // a comparison gets the expression reader's own complaint instead.
        var written = step.Text(name).Trim();
        if (written.Length > 0
            && !Expression.TryEvaluate(written, Variables, out _, out var error)
            && written.Any(character => character is '=' or '<' or '>' or '!'))
        {
            throw error!;
        }

        return ReadValue(step, name).AsBool();
    }

    /// <summary>
    /// Whether one step of the macro ended up doing what a condition says it should have. The
    /// answer is read from the step's outcome, which the run writes as it goes — so a step the run
    /// has not reached yet answers "not reached" rather than "no", and a condition about a step
    /// that is not in the macro at all is a mistake rather than a quiet false.
    /// </summary>
    private bool StepResult(ExecutableStep condition)
    {
        var id = ReadText(condition, "stepId").Trim();
        if (id.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingStep);
        }

        var expected = ReadText(condition, "expectedOutcome").Trim();
        if (!Variables.TryGet(OutcomeName(id), out var outcome))
        {
            throw new StepFailure(RunKeys.NoSuchStep, id);
        }

        return string.Equals(outcome.AsText().Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether a list holds a value. The list is read the way every other list field is, so it can
    /// be written out or named by a variable holding one; the value is compared the way an
    /// equality test compares, so "1" finds 1 and text is not told apart by case.
    /// </summary>
    private bool ListHolds(ExecutableStep condition)
    {
        if (!ListText.TryParse(ReadText(condition, "list"), Variables, out var items, out var error))
        {
            throw new StepFailure(RunKeys.BadList, error?.Detail ?? ReadText(condition, "list"));
        }

        var wanted = ReadValue(condition, "wantedValue");
        return items.Any(item => ValueRules.Same(item, wanted)
            || string.Equals(item.AsText(), wanted.AsText(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Whether a window answering to a title or to a program's name is open. An empty value means
    /// "any window at all", which is how a macro waits for something to come up without knowing
    /// what Windows will call it.
    /// </summary>
    private bool WindowThere(ExecutableStep condition)
    {
        var rule = WindowRuleOf(condition);
        return _devices.Windows.Find(rule.MatchValue, rule.Reading.Match, rule.Reading.Compare,
            rule.Options) is not null;
    }

    /// <summary>
    /// Whether a value sits between two others. Both ends count as inside, so a range of 1 to 10
    /// holds 1 and 10 — a range written with its ends included is what a number line looks like,
    /// and the hint says so where the macro is written.
    /// </summary>
    private bool InRange(ExecutableStep condition)
    {
        var value = ReadValue(condition, "testValue").AsNumber();
        var low = ReadValue(condition, "min").AsNumber();
        var high = ReadValue(condition, "max").AsNumber();
        return value >= Math.Min(low, high) && value <= Math.Max(low, high);
    }

    /// <summary>
    /// Whether one date is before, after or the same as another. Both sides are read the way every
    /// date function reads one, so <c>$sys.dateTime</c>, <c>2026-01-01</c> and <c>$today()</c> all
    /// work; "the same day" ignores the time of day, which is what "has this run today already"
    /// needs and what comparing two timestamps cannot answer.
    /// </summary>
    private bool DatesCompared(ExecutableStep condition)
    {
        var left = FunctionLibrary.Moment(DateText(ReadText(condition, "left")));
        var right = FunctionLibrary.Moment(DateText(ReadText(condition, "right")));
        return ReadText(condition, "dateOperator").Trim().ToLowerInvariant() switch
        {
            "before" => left < right,
            "after" => left > right,
            "sameday" => left.Date == right.Date,
            _ => left == right,
        };
    }

    /// <summary>
    /// Reads one side of a date comparison. A date written out is taken as written: 2026-01-31 is
    /// that day and not the arithmetic 2026 minus 1 minus 31, which is what every other value field
    /// would make of it — and a date that quietly turned into 1994 would be a bug nobody could see.
    /// A name, a <c>$name</c> or something plainly worked out, such as <c>today()</c>, is read the
    /// way every other value is.
    /// </summary>
    private string DateText(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        if (trimmed.StartsWith('$') || trimmed.Contains('('))
        {
            return Read(trimmed).AsText();
        }

        return Variables.TryGet(trimmed, out var named) ? named.AsText() : trimmed;
    }

    /// <summary>True when a reference picture is somewhere in a condition's search areas.</summary>
    private bool ImageThere(ExecutableStep condition) => Search(condition) is not null;

    /// <summary>
    /// True when the text a condition names is somewhere in its search areas. The negative wording
    /// is a condition of its own rather than a switch on this one, so what a step means is written
    /// in its name instead of hidden in a checkbox.
    /// </summary>
    private bool TextThere(ExecutableStep condition)
    {
        var wanted = ReadValue(condition, "queryText").AsText();
        var way = TextWay(condition, wanted);
        return ReadSpans(condition).Any(span => Matches(condition, span.Text, wanted, way));
    }

    /// <summary>
    /// The way a step looks for text, read and checked before anything is looked at: a word that is
    /// none of the five, or a pattern the machine cannot read, is refused here rather than after a
    /// screen has been read and compared.
    /// </summary>
    private static string TextWay(ExecutableStep step, string wanted)
    {
        var way = Way(step.Text("matchMode"));
        try
        {
            TextCompare.Check(wanted, way);
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, wanted);
        }

        return way;
    }

    /// <summary>
    /// Answers whether the points a condition names show the colours it names. Each entry is one
    /// "x,y,#RRGGBB", separated by a semicolon or a line break, and the mode decides whether every
    /// point has to match or one is enough.
    /// </summary>
    private bool ColoursMatch(ExecutableStep condition)
    {
        var tolerance = ReadValue(condition, "tolerance").AsNumber();
        var every = !string.Equals(ReadText(condition, "colorMatchMode").Trim(), "any",
            StringComparison.OrdinalIgnoreCase);
        var window = AnchorOf(condition).Window;

        var matched = Points(condition)
            .Select(point => PixelAt(condition, point.Where, window)
                .Matches(point.Colour, tolerance));

        return every ? matched.All(match => match) : matched.Any(match => match);
    }

    /// <summary>One point of a colour comparison: where it is, and the colour it has to show.</summary>
    private sealed record ColourPoint(ScreenPoint Where, PixelColor Colour);

    /// <summary>
    /// Reads a list of "x,y,#RRGGBB" points, separated by a semicolon or a line break. Each one is
    /// resolved where it is read, so the whole list can be counted from a window's corner.
    /// </summary>
    private IReadOnlyList<ColourPoint> Points(ExecutableStep step)
    {
        var text = ReadText(step, "points").Trim();
        var points = new List<ColourPoint>();
        foreach (var entry in text.Split([';', '\n', '\r'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length != 3
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y)
                || !IsColour(parts[2]))
            {
                throw new StepFailure(RunKeys.BadPoints, entry);
            }

            points.Add(new ColourPoint(Place(step, x, y), PixelColor.Parse(parts[2])));
        }

        if (points.Count == 0)
        {
            throw new StepFailure(RunKeys.BadPoints, text);
        }

        return points;
    }

    /// <summary>True when the text reads as a #RRGGBB or #RGB colour.</summary>
    private static bool IsColour(string text)
    {
        var value = text.Trim().TrimStart('#');
        return value.Length is 3 or 6 && value.All(char.IsAsciiHexDigit);
    }

    private bool Compare(ExecutableStep step)
    {
        var left = ReadValue(step, "variable");
        var right = ReadValue(step, "compareValue");
        var same = ValueRules.Same(left, right)
            || Compare(step, left, right, TextCompare.Exactly);

        return ReadText(step, "compareOperator") switch
        {
            "equals" => same,
            "notEquals" => !same,
            "greaterThan" => left.AsNumber() > right.AsNumber(),
            "greaterOrEqual" => left.AsNumber() >= right.AsNumber(),
            "lessThan" => left.AsNumber() < right.AsNumber(),
            "lessOrEqual" => left.AsNumber() <= right.AsNumber(),
            // The text comparisons are the ones the whole program shares, so a pattern in a condition
            // and the same pattern in a text search cannot come to mean two different things.
            TextCompare.Contains => Compare(step, left, right, TextCompare.Contains),
            "notContains" => !Compare(step, left, right, TextCompare.Contains),
            "exists" => !left.IsNull && left.AsText().Length > 0,
            TextCompare.Regex => Compare(step, left, right, TextCompare.Regex),
            _ => false,
        };
    }

    /// <summary>
    /// Holds two values together the way <see cref="TextCompare"/> says, turning a pattern the machine
    /// cannot read into a failed step rather than a stack trace — the same answer the steps that look
    /// for text give.
    /// </summary>
    private bool Compare(ExecutableStep step, Value left, Value right, string way)
    {
        try
        {
            return TextCompare.Matches(left.AsText(), right.AsText(), way,
                OptionsOf(step, way));
        }
        catch (ArgumentException)
        {
            throw new StepFailure(RunKeys.BadPattern, right.AsText());
        }
    }
}
