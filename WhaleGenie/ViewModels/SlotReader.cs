using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.ViewModels;

// Reading a written formula into those pieces.
/// <summary>
/// Reads the formula as a run of pieces rather than as a tree: a name, a number, a string, a sign,
/// a bracket, or a call with its arguments split at the commas that stand outside any bracket. There
/// is no "does not read" here — a character nobody knows is still a piece, shown where it stands and
/// removable like anything else. What the engine makes of the line is said under the band, not by
/// dropping half of it.
///
/// <para>
/// The pieces are cut by the engine's own scanner. A piece therefore begins and ends where a token
/// begins and ends, which is what keeps the band and the parser from disagreeing about the line.
/// </para>
/// </summary>
internal sealed class SlotReader
{
    private readonly string _text;
    private readonly IReadOnlyList<ExpressionToken> _tokens;
    private readonly IReadOnlyDictionary<string, ExpressionFunction> _functions;
    private readonly bool _bareNames;

    private SlotReader(string text, IReadOnlyList<ExpressionToken> tokens,
        IReadOnlyDictionary<string, ExpressionFunction> functions, bool bareNames)
    {
        _text = text;
        _tokens = tokens;
        _functions = functions;
        _bareNames = bareNames;
    }

    public static SlotNode Read(string text, IReadOnlyDictionary<string, ExpressionFunction> functions,
        bool bareNames = false)
    {
        var tokens = ExpressionTokens.Scan(text, strict: false);
        var root = new SlotReader(text, tokens, functions, bareNames)
            .Sequence(0, tokens.Count - 1, Strings.Get("ValuePicker.Hole"));

        foreach (var piece in root.Walk())
        {
            if (piece.Length > 0 && piece is not (SlotEmpty or SlotSequence))
            {
                piece.CanRemove = true;
            }
        }

        return root;
    }

    /// <summary>One run of pieces between two token indices. Commas belong to the call, not to the run.</summary>
    private SlotSequence Sequence(int from, int to, string argument)
    {
        var items = new List<SlotNode>();
        var at = from;
        while (at < to)
        {
            var token = _tokens[at];
            if (token.Kind is ExpressionTokenKind.Symbol && token.Text == ",")
            {
                at++;
                continue;
            }

            var piece = Piece(at, to, out var next);
            if (piece is null)
            {
                at++;
                continue;
            }

            items.Add(piece);
            at = Math.Max(at + 1, next);
        }

        // An argument with nothing in it is still a place: it is what a call shows while an argument
        // is still to be written.
        if (items.Count == 0 && argument.Length > 0)
        {
            items.Add(new SlotEmpty
            {
                Text = argument,
                Hint = argument,
                Start = StartOf(from),
                Length = 0,
                Required = true,
            });
        }

        return new SlotSequence
        {
            Items = items,
            Start = StartOf(from),
            Length = Math.Max(0, EndOf(to) - StartOf(from)),
        };
    }

    /// <summary>One token as a piece, and where the next piece begins.</summary>
    private SlotNode? Piece(int at, int to, out int next)
    {
        var token = _tokens[at];
        next = at + 1;

        switch (token.Kind)
        {
            case ExpressionTokenKind.Number:
                return new SlotValue
                {
                    Text = token.Text,
                    Tone = "maths",
                    Start = token.Start,
                    Length = token.Length,
                };

            case ExpressionTokenKind.Text:
                return new SlotValue
                {
                    Text = _text.Substring(token.Start, token.Length),
                    Tone = "maths",
                    Start = token.Start,
                    Length = token.Length,
                };

            case ExpressionTokenKind.Variable:
                return new SlotValue
                {
                    Text = _text.Substring(token.Start, token.Length),
                    Tone = "lists",
                    IsName = true,
                    Start = token.Start,
                    Length = token.Length,
                };

            case ExpressionTokenKind.Name:
                if (next < to
                    && _tokens[next].Kind is ExpressionTokenKind.Symbol
                    && _tokens[next].Text == "(")
                {
                    return Call(at, to, out next);
                }

                if (ExpressionTokens.IsOperatorWord(token.Text))
                {
                    return new SlotOperator
                    {
                        Text = token.Text,
                        Start = token.Start,
                        Length = token.Length,
                    };
                }

                var constant = token.Text is "true" or "false" or "null";
                return new SlotValue
                {
                    Text = token.Text,
                    Tone = constant ? "other" : "lists",
                    IsName = !constant && _bareNames,
                    Start = token.Start,
                    Length = token.Length,
                };

            case ExpressionTokenKind.Symbol:
                return token.Text is "(" or ")" or "[" or "]"
                    ? new SlotBracket
                    {
                        Text = token.Text,
                        Start = token.Start,
                        Length = token.Length,
                    }
                    : new SlotOperator
                    {
                        Text = token.Text,
                        Start = token.Start,
                        Length = token.Length,
                    };

            case ExpressionTokenKind.Unknown:
                return new SlotOperator
                {
                    Text = token.Text,
                    Start = token.Start,
                    Length = token.Length,
                };

            default:
                next = at;
                return null;
        }
    }

    /// <summary>
    /// A call: the name, the brackets, and one sequence per argument — split at the commas that stand
    /// outside every bracket inside it. What is inside an argument is read the same way as the whole,
    /// so a call in an argument is just another piece in that run.
    /// </summary>
    private SlotNode Call(int nameIndex, int to, out int next)
    {
        var nameToken = _tokens[nameIndex];
        var name = nameToken.Text;
        var open = nameIndex + 1;
        var close = Closing(open, to);
        var insideFrom = open + 1;
        var insideTo = close >= 0 ? close : to;

        var arguments = new List<SlotSequence>();
        var from = insideFrom;
        var depth = 0;
        for (var index = insideFrom; index < insideTo; index++)
        {
            var token = _tokens[index];
            if (token.Kind is not ExpressionTokenKind.Symbol)
            {
                continue;
            }

            if (token.Text is "(" or "[")
            {
                depth++;
                continue;
            }

            if (token.Text is ")" or "]")
            {
                depth--;
                continue;
            }

            if (depth == 0 && token.Text == ",")
            {
                arguments.Add(Sequence(from, index, ArgumentName(name, arguments.Count)));
                from = index + 1;
            }
        }

        if (insideFrom < insideTo || arguments.Count > 0)
        {
            arguments.Add(Sequence(from, insideTo, ArgumentName(name, arguments.Count)));
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

        var ghostAt = close >= 0 ? _tokens[close].Start : _text.Length;
        var ghost = entry is not null && arguments.Count >= entry.MaxArguments
            ? null
            : new SlotEmpty
            {
                Text = Strings.Get("ValuePicker.Slot.Add"),
                Hint = entry is not null && arguments.Count < entry.Parameters.Count
                    ? ArgumentName(name, arguments.Count)
                    : Strings.Get("ValuePicker.Slot.AddHint"),
                Start = ghostAt,
                Before = arguments.Count == 0 ? string.Empty : ", ",
                IsGhost = true,
                Required = entry is null ? arguments.Count == 0 : arguments.Count < entry.MinArguments,
            };

        next = close >= 0 ? close + 1 : to;
        var end = close >= 0 ? _tokens[close].End : _text.Length;
        return new SlotCall
        {
            Name = name,
            Tone = ToneOf(name),
            Arguments = arguments,
            Ghost = ghost,
            Start = nameToken.Start,
            Length = Math.Max(0, end - nameToken.Start),
        };
    }

    /// <summary>The closing bracket that matches this one, or -1 when the text never closes it.</summary>
    private int Closing(int open, int to)
    {
        var depth = 0;
        for (var index = open; index < to; index++)
        {
            var token = _tokens[index];
            if (token.Kind is not ExpressionTokenKind.Symbol)
            {
                continue;
            }

            if (token.Text is "(" or "[")
            {
                depth++;
            }
            else if (token.Text is ")" or "]")
            {
                depth--;
                if (depth == 0)
                {
                    return token.Text == ")" ? index : -1;
                }
            }
        }

        return -1;
    }

    private int StartOf(int at) => at < _tokens.Count ? _tokens[at].Start : _text.Length;

    private int EndOf(int to) => to > 0 && to <= _tokens.Count ? _tokens[to - 1].End : 0;

    /// <summary>What the place at this position is called: the argument's own name where there is one.</summary>
    private string ArgumentName(string function, int index)
        => _functions.TryGetValue(function, out var entry) && index < entry.Parameters.Count
            ? ExpressionText.ParameterLabel(function, entry.Parameters[index])
            : Strings.Get("ValuePicker.Hole");

    private string ToneOf(string name)
        => _functions.TryGetValue(name, out var entry) ? entry.Group : "other";
}
