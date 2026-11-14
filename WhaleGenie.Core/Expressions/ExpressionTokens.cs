using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WhaleGenie.Core.Expressions;

/// <summary>What a piece of a line is: a number, a piece of text, a name, a sign, or something the
/// language does not know. A name is a name on its own; there is no sign in front of one.</summary>
public enum ExpressionTokenKind
{
    Number,

    /// <summary>A date written in the language's one shape, such as <c>2026-11-12</c>.</summary>
    Date,

    Text,
    Name,
    Symbol,
    Unknown,
    End,
}

/// <summary>
/// One piece of a line and where it stands in it. The editor draws its pieces from these spans, so
/// the band and the parser cannot disagree about where a piece begins or ends.
/// </summary>
public readonly record struct ExpressionToken(
    ExpressionTokenKind Kind, int Start, int Length, string Text, double Number)
{
    public int End => Start + Length;
}

/// <summary>
/// Splits expression text into the tokens the reader walks over. One scanner serves both readers:
/// the parser asks for strict tokens, and the editor asks for tolerant ones so a half-written line
/// can still be drawn. The rules are the language's own, in one place.
/// </summary>
public static class ExpressionTokens
{
    /// <summary>
    /// Cuts a line into tokens. <paramref name="strict"/> says whether a character the language
    /// cannot read is an error or a piece the editor can show and let the person remove.
    /// </summary>
    public static IReadOnlyList<ExpressionToken> Scan(string source, bool strict = true)
    {
        var tokens = new List<ExpressionToken>();
        var index = 0;

        while (index < source.Length)
        {
            var character = source[index];
            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (char.IsDigit(character)
                || (character == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1])))
            {
                // A date is one value, not three numbers with two subtractions between them. It is
                // read before the number branch so 2026-11-12 cannot become 2003.
                if (TryReadDate(source, index, out var dateEnd, out var dateText))
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Date, index,
                        dateEnd - index, dateText, 0));
                    index = dateEnd;
                    continue;
                }

                var start = index;
                while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.'))
                {
                    index++;
                }

                var text = source[start..index];
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Number, start, index - start,
                        text, number));
                }
                else if (strict)
                {
                    throw ExpressionException.Syntax($"\"{text}\" is not a number");
                }
                else
                {
                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Number, start, index - start,
                        text, 0));
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                index = ReadText(source, index, character, tokens, strict);
                continue;
            }

            if (char.IsLetter(character) || character == '_')
            {
                var start = index;
                while (index < source.Length
                    && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '.'))
                {
                    // A dot joins a name to the field it reads, and that field may begin with a
                    // digit: step.1.outcome is one name, just as where.age is. A dot with nothing
                    // after it starts the next word instead.
                    if (source[index] == '.'
                        && (index + 1 >= source.Length
                            || !(char.IsLetterOrDigit(source[index + 1])
                                || source[index + 1] == '_')))
                    {
                        break;
                    }

                    index++;
                }

                tokens.Add(new ExpressionToken(ExpressionTokenKind.Name, start, index - start,
                    source[start..index], 0));
                continue;
            }

            var pair = index + 1 < source.Length ? source.Substring(index, 2) : string.Empty;
            if (pair is "==" or "!=" or "<=" or ">=" or "&&" or "||")
            {
                tokens.Add(new ExpressionToken(ExpressionTokenKind.Symbol, index, 2, pair, 0));
                index += 2;
                continue;
            }

            if ("+-*/%(),[]<>!=".Contains(character))
            {
                if (character == '=' && strict)
                {
                    throw ExpressionException.Syntax("\"=\" on its own; write \"==\" to compare");
                }

                tokens.Add(new ExpressionToken(ExpressionTokenKind.Symbol, index, 1,
                    character.ToString(), 0));
                index++;
                continue;
            }

            // Braces frame a name or a formula in a line the field keeps as text. The editor draws
            // them; the parser never sees a strict line because the text reader handles them first.
            if (!strict && character is '{' or '}')
            {
                tokens.Add(new ExpressionToken(ExpressionTokenKind.Symbol, index, 1,
                    character.ToString(), 0));
                index++;
                continue;
            }

            if (strict)
            {
                throw ExpressionException.Syntax($"unexpected character \"{character}\"");
            }

            tokens.Add(new ExpressionToken(ExpressionTokenKind.Unknown, index, 1,
                character.ToString(), 0));
            index++;
        }

        tokens.Add(new ExpressionToken(ExpressionTokenKind.End, source.Length, 0, string.Empty, 0));
        return tokens;
    }

    /// <summary>True for the three words that name a sign rather than a value.</summary>
    public static bool IsOperatorWord(string text)
        => string.Equals(text, "and", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "or", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "not", StringComparison.OrdinalIgnoreCase);

    private static int ReadText(string source, int index, char quote,
        List<ExpressionToken> tokens, bool strict)
    {
        var start = index;
        var builder = new StringBuilder();
        index++;

        while (index < source.Length)
        {
            var current = source[index];
            if (current == '\\' && index + 1 < source.Length)
            {
                builder.Append(source[index + 1] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    var escaped => escaped,
                });
                index += 2;
                continue;
            }

            if (current == quote)
            {
                index++;
                tokens.Add(new ExpressionToken(ExpressionTokenKind.Text, start, index - start,
                    builder.ToString(), 0));
                return index;
            }

            builder.Append(current);
            index++;
        }

        if (strict)
        {
            throw ExpressionException.Syntax("a closing quote is missing");
        }

        tokens.Add(new ExpressionToken(ExpressionTokenKind.Text, start, source.Length - start,
            builder.ToString(), 0));
        return source.Length;
    }

    /// <summary>
    /// Reads the one shape a date is written in: four digits of year, a month, a day, and then
    /// optionally a time after a space or a <c>T</c>. A shorter year, a missing part, a day that
    /// does not exist, or a date with something stuck to its end — <c>2026-11-12.csv</c> — is not a
    /// date, and is left to the reader as the numbers and signs it was written as.
    /// </summary>
    private static bool TryReadDate(string source, int start, out int end, out string text)
    {
        end = start;
        text = string.Empty;

        if (start + 10 > source.Length)
        {
            return false;
        }

        for (var at = start; at < start + 4; at++)
        {
            if (!char.IsAsciiDigit(source[at]))
            {
                return false;
            }
        }

        if (source[start + 4] != '-')
        {
            return false;
        }

        var monthAt = start + 5;
        var monthEnd = Digits(source, monthAt, 2);
        if (monthEnd == monthAt || monthEnd >= source.Length || source[monthEnd] != '-')
        {
            return false;
        }

        var dayAt = monthEnd + 1;
        var dayEnd = Digits(source, dayAt, 2);
        if (dayEnd == dayAt)
        {
            return false;
        }

        var year = int.Parse(source[start..(start + 4)], CultureInfo.InvariantCulture);
        var month = int.Parse(source[monthAt..monthEnd], CultureInfo.InvariantCulture);
        var day = int.Parse(source[dayAt..dayEnd], CultureInfo.InvariantCulture);
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        var stop = dayEnd;
        if (stop < source.Length && source[stop] is ' ' or 'T')
        {
            var timeAt = stop + 1;
            var timeEnd = Digits(source, timeAt, 2);
            if (timeEnd - timeAt == 2 && timeEnd < source.Length && source[timeEnd] == ':')
            {
                var minuteAt = timeEnd + 1;
                var minuteEnd = Digits(source, minuteAt, 2);
                var hour = int.Parse(source[timeAt..timeEnd], CultureInfo.InvariantCulture);
                var minute = int.Parse(source[minuteAt..minuteEnd], CultureInfo.InvariantCulture);
                if (minuteEnd - minuteAt == 2 && hour < 24 && minute < 60)
                {
                    var secondsEnd = minuteEnd;
                    if (secondsEnd + 3 <= source.Length
                        && source[secondsEnd] == ':'
                        && Digits(source, secondsEnd + 1, 2) == secondsEnd + 3)
                    {
                        var second = int.Parse(source[(secondsEnd + 1)..(secondsEnd + 3)],
                            CultureInfo.InvariantCulture);
                        if (second < 60)
                        {
                            secondsEnd += 3;
                        }
                    }

                    stop = secondsEnd;
                }
            }
        }

        // A date is a value of its own only when nothing is stuck to it: 2026-11-12.csv names a
        // file, and 2026-11-12abc is a name somebody typed.
        if (stop < source.Length && (char.IsLetterOrDigit(source[stop]) || source[stop] is '_' or '.'))
        {
            return false;
        }

        end = stop;
        text = source[start..stop];
        return true;
    }

    /// <summary>The place after one or two digits, or the place it started at when there are none.</summary>
    private static int Digits(string source, int at, int most)
    {
        var end = at;
        while (end < source.Length && end - at < most && char.IsAsciiDigit(source[end]))
        {
            end++;
        }

        return end;
    }
}
