using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.ViewModels;

// Reading a written formula into those pieces.
/// <summary>
/// Reads the formula as a run of pieces rather than as a tree: a name, a number, a string, a sign, a
/// bracket, or a call with its arguments split at the commas that stand outside any bracket. There
/// is no "does not read" here — a character nobody knows is still a piece, shown where it stands and
/// removable like anything else. What the engine makes of the line is said under the band, not by
/// dropping half of it.
/// </summary>
internal sealed class SlotReader
{
    /// <summary>Signs, longest first so <c>&gt;=</c> is never read as <c>&gt;</c> then <c>=</c>.</summary>
    private static readonly string[] Signs =
        ["==", "!=", ">=", "<=", "&&", "||", "+", "-", "*", "/", "%", ">", "<", "!", "="];

    private readonly string _text;
    private readonly IReadOnlyDictionary<string, ExpressionFunction> _functions;

    private SlotReader(string text, IReadOnlyDictionary<string, ExpressionFunction> functions)
    {
        _text = text;
        _functions = functions;
    }

    public static SlotNode Read(string text, IReadOnlyDictionary<string, ExpressionFunction> functions)
    {
        var root = new SlotReader(text, functions)
            .Sequence(0, text.Length, Strings.Get("ValuePicker.Hole"));

        foreach (var piece in root.Walk())
        {
            if (piece.Length > 0 && piece is not (SlotEmpty or SlotSequence))
            {
                piece.CanRemove = true;
            }
        }

        return root;
    }

    /// <summary>One run of pieces between two offsets. Commas belong to the call, not to the run.</summary>
    private SlotSequence Sequence(int from, int to, string argument)
    {
        var items = new List<SlotNode>();
        var at = from;
        while (at < to)
        {
            at = Spaces(at, to);
            if (at >= to)
            {
                break;
            }

            if (_text[at] == ',')
            {
                at++;
                continue;
            }

            var token = Token(at, to);
            if (token is null)
            {
                at++;
                continue;
            }

            items.Add(token);
            at = Math.Max(at + 1, token.Start + token.Length);
        }

        // An argument with nothing in it is still a place: it is what a call shows while an argument
        // is still to be written.
        if (items.Count == 0 && argument.Length > 0)
        {
            items.Add(new SlotEmpty
            {
                Text = argument,
                Hint = argument,
                Start = from,
                Length = 0,
                Required = true,
            });
        }

        return new SlotSequence { Items = items, Start = from, Length = Math.Max(0, to - from) };
    }

    private SlotNode? Token(int at, int to)
    {
        var character = _text[at];

        if (char.IsDigit(character)
            || (character == '.' && at + 1 < to && char.IsDigit(_text[at + 1])))
        {
            return Number(at, to);
        }

        if (character is '"' or '\'')
        {
            return Text(at, to, character);
        }

        if (character == '$')
        {
            return Name(at, to);
        }

        if (character is '(' or ')' or '[' or ']')
        {
            return new SlotBracket { Text = character.ToString(), Start = at, Length = 1 };
        }

        if (char.IsLetter(character) || character == '_')
        {
            return Word(at, to);
        }

        foreach (var sign in Signs)
        {
            if (at + sign.Length <= to
                && string.CompareOrdinal(_text, at, sign, 0, sign.Length) == 0)
            {
                return new SlotOperator { Text = sign, Start = at, Length = sign.Length };
            }
        }

        // A character nobody knows is still a piece. Dropping it would make the band a picture of a
        // shorter formula than the field holds.
        return new SlotOperator { Text = character.ToString(), Start = at, Length = 1 };
    }

    private SlotNode Number(int at, int to)
    {
        var end = at;
        while (end < to && (char.IsDigit(_text[end]) || _text[end] == '.'))
        {
            end++;
        }

        return new SlotValue { Text = _text[at..end], Tone = "maths", Start = at, Length = end - at };
    }

    private SlotNode Text(int at, int to, char quote)
    {
        var end = at + 1;
        while (end < to)
        {
            if (_text[end] == '\\')
            {
                end += 2;
                continue;
            }

            if (_text[end] == quote)
            {
                end++;
                break;
            }

            end++;
        }

        return new SlotValue
        {
            Text = _text[at..Math.Min(end, to)],
            Tone = "maths",
            Start = at,
            Length = Math.Min(end, to) - at,
        };
    }

    private SlotNode Name(int at, int to)
    {
        var end = at + 1;
        if (end < to && VariableText.IsNameStart(_text[end]))
        {
            end++;
            while (end < to && VariableText.IsNameCharacter(_text[end]))
            {
                end++;
            }
        }

        return new SlotValue
        {
            Text = _text[at..end],
            Tone = "lists",
            Start = at,
            Length = end - at,
        };
    }

    private SlotNode Word(int at, int to)
    {
        var end = at + 1;
        while (end < to && (char.IsLetterOrDigit(_text[end]) || _text[end] == '_'))
        {
            end++;
        }

        var word = _text[at..end];
        var after = Spaces(end, to);

        if (after < to && _text[after] == '(')
        {
            return Call(at, end, after, to);
        }

        if (word is "and" or "or" or "not")
        {
            return new SlotOperator { Text = word, Start = at, Length = end - at };
        }

        return new SlotValue
        {
            Text = word,
            Tone = word is "true" or "false" or "null" ? "other" : "lists",
            Start = at,
            Length = end - at,
        };
    }

    /// <summary>
    /// A call: the name, the brackets, and one sequence per argument — split at the commas that stand
    /// outside every bracket inside it. What is inside an argument is read the same way as the whole,
    /// so a call in an argument is just another piece in that run.
    /// </summary>
    private SlotNode Call(int start, int nameEnd, int paren, int to)
    {
        var name = _text[start..nameEnd];
        var close = Closing(paren, to);
        var end = close >= 0 ? close + 1 : to;
        var inside = paren + 1;
        var limit = close >= 0 ? close : to;

        var arguments = new List<SlotSequence>();
        var from = inside;
        var depth = 0;
        var quote = '\0';

        for (var index = inside; index < limit; index++)
        {
            var character = _text[index];
            if (quote != '\0')
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character is '(' or '[')
            {
                depth++;
                continue;
            }

            if (character is ')' or ']')
            {
                depth--;
                continue;
            }

            if (character == ',' && depth == 0)
            {
                arguments.Add(Sequence(from, index, ArgumentName(name, arguments.Count)));
                from = index + 1;
            }
        }

        if (inside < limit || arguments.Count > 0)
        {
            arguments.Add(Sequence(from, limit, ArgumentName(name, arguments.Count)));
        }

        for (var index = 1; index < arguments.Count; index++)
        {
            arguments[index].Before = ", ";
        }

        _functions.TryGetValue(name, out var entry);

        // The band says what each written argument is for, not only what the empty ones want. The
        // function table is the same one the engine checks the call with, so the two cannot disagree.
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            argument.ArgumentIndex = index + 1;
            argument.ArgumentLabel = entry is not null && index < entry.Parameters.Count
                ? ArgumentName(name, index)
                : Strings.Format("ValuePicker.Arg.Ordinal", index + 1);
            argument.ArgumentRequired = entry is not null && index < entry.MinArguments;
            argument.ArgumentOptional = entry is not null && index >= entry.MinArguments;
            argument.ArgumentFlag = entry is null
                ? string.Empty
                : argument.ArgumentRequired
                    ? Strings.Get("ValuePicker.Arg.Required")
                    : Strings.Get("ValuePicker.Arg.Optional");
        }

        var ghost = entry is not null && arguments.Count >= entry.MaxArguments
            ? null
            : new SlotEmpty
            {
                Text = Strings.Get("ValuePicker.Slot.Add"),
                Hint = entry is not null && arguments.Count < entry.Parameters.Count
                    ? ArgumentName(name, arguments.Count)
                    : Strings.Get("ValuePicker.Slot.AddHint"),
                Start = limit,
                Before = arguments.Count == 0 ? string.Empty : ", ",
                IsGhost = true,
                Required = entry is null ? arguments.Count == 0 : arguments.Count < entry.MinArguments,
            };

        return new SlotCall
        {
            Name = name,
            Tone = ToneOf(name),
            Arguments = arguments,
            Ghost = ghost,
            Start = start,
            Length = Math.Max(0, end - start),
        };
    }

    /// <summary>The closing bracket that matches this one, or -1 when the text never closes it.</summary>
    private int Closing(int paren, int to)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = paren; index < to; index++)
        {
            var character = _text[index];
            if (quote != '\0')
            {
                if (character == '\\')
                {
                    index++;
                }
                else if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
                continue;
            }

            if (character is '(' or '[')
            {
                depth++;
            }
            else if (character is ')' or ']')
            {
                depth--;
                if (depth == 0)
                {
                    return character == ')' ? index : -1;
                }
            }
        }

        return -1;
    }

    private int Spaces(int at, int to)
    {
        while (at < to && char.IsWhiteSpace(_text[at]))
        {
            at++;
        }

        return at;
    }

    /// <summary>What the place at this position is called: the argument's own name where there is one.</summary>
    private string ArgumentName(string function, int index)
        => _functions.TryGetValue(function, out var entry) && index < entry.Parameters.Count
            ? ExpressionText.ParameterLabel(function, entry.Parameters[index])
            : Strings.Get("ValuePicker.Hole");

    private string ToneOf(string name)
        => _functions.TryGetValue(name, out var entry) ? entry.Group : "other";
}
