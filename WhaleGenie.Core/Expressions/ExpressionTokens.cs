using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Expressions;

/// <summary>What a piece of a line is: a number, a piece of text, a name, a variable, a sign, or
/// something the language does not know.</summary>
public enum ExpressionTokenKind
{
    Number,
    Text,
    Variable,
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

            if (character == '$')
            {
                if (index + 1 < source.Length && source[index + 1] == '$')
                {
                    if (strict)
                    {
                        throw ExpressionException.Syntax("a variable name is missing after \"$\"");
                    }

                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Symbol, index, 2, "$$", 0));
                    index += 2;
                    continue;
                }

                if (index + 1 < source.Length && source[index + 1] == '{')
                {
                    if (strict)
                    {
                        throw ExpressionException.Syntax("a variable name is missing after \"$\"");
                    }

                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Symbol, index, 2, "${", 0));
                    index += 2;
                    continue;
                }

                var dollar = index;
                index++;
                if (index >= source.Length || !VariableName.IsStart(source[index]))
                {
                    if (strict)
                    {
                        throw ExpressionException.Syntax("a variable name is missing after \"$\"");
                    }

                    tokens.Add(new ExpressionToken(ExpressionTokenKind.Variable, dollar, 1,
                        string.Empty, 0));
                    continue;
                }

                var start = index;
                while (index < source.Length && VariableName.IsPart(source[index]))
                {
                    index++;
                }

                tokens.Add(new ExpressionToken(ExpressionTokenKind.Variable, dollar,
                    index - dollar, source[start..index], 0));
                continue;
            }

            if (char.IsLetter(character) || character == '_')
            {
                var start = index;
                while (index < source.Length
                    && (char.IsLetterOrDigit(source[index]) || source[index] is '_' or '.'))
                {
                    // A dot joins a name to the field it reads — <c>where.age</c> is one name — and
                    // starts the next word rather than continuing this one everywhere else.
                    if (source[index] == '.'
                        && (index + 1 >= source.Length || !char.IsLetter(source[index + 1])))
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
}
