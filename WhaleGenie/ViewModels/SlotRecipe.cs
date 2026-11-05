using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One piece of a formula as the structure band draws it. There are only a few kinds: a value, a
/// sign, a bracket, and a call — which is a container holding one <see cref="SlotSequence"/> per
/// argument, so a call inside a call is drawn inside it. Nothing else is a shape, because nothing
/// else needs to be: whatever the text holds, it comes out as one of these.
///
/// Every piece remembers where it starts in the formula text. The band never writes the text from
/// these pieces; it cuts and pastes spans of the text itself, so what the person sees and what the
/// field keeps cannot drift apart.
/// </summary>
public abstract partial class SlotNode : ViewModelBase
{
    /// <summary>Where this piece begins in the formula, counted from the first character.</summary>
    public int Start { get; init; }

    /// <summary>How many characters it covers. An empty place covers none.</summary>
    public int Length { get; init; }

    /// <summary>
    /// What stands in front of it inside its parent: the comma between two arguments. Written on the
    /// child because the child is what a template draws, and a function body is one row of children.
    /// </summary>
    public string Before { get; set; } = string.Empty;

    /// <summary>True for a place nothing has been put in yet.</summary>
    public bool IsEmpty => Length <= 0;

    /// <summary>
    /// True for a piece that can be taken away with its cross. Sequences are not pieces: they are
    /// what pieces sit in, and taking one away is taking away what is in it.
    /// </summary>
    public bool CanRemove { get; set; }

    /// <summary>True on the piece the pointer last chose, which the band paints as the one being filled.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsRemove))]
    public partial bool IsChosen { get; set; }

    /// <summary>
    /// True on the piece the pointer is over right now. The window keeps this rather than the band
    /// relying on <c>:pointerover</c>: that pseudo-class is raised on every ancestor of what the
    /// pointer hit, so hovering one name would light the cross of every box it sits inside, and a
    /// drag clears the whole band's pointerover the moment the pointer is captured.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsRemove))]
    public partial bool IsHovered { get; set; }

    /// <summary>
    /// True when this piece wears a cross. A cross is only worth showing on what the pointer is on
    /// (or what was chosen): with a cross on every piece at once the line is a thicket of them.
    /// </summary>
    public bool ShowsRemove => CanRemove && (IsHovered || IsChosen);

    /// <summary>Every piece of this shape, itself first, so one choice can clear all the others.</summary>
    public IEnumerable<SlotNode> Walk()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var piece in child.Walk())
            {
                yield return piece;
            }
        }
    }

    /// <summary>The pieces directly inside this one, in the order they are written.</summary>
    protected virtual IEnumerable<SlotNode> Children => [];
}

/// <summary>
/// A run of pieces with nothing wrapping it: the whole formula, or one argument of a call. This is
/// the "free sub-sequence" — values, signs, brackets and further calls can be put in it in any
/// order, and the band simply draws them one after another.
/// </summary>
public sealed class SlotSequence : SlotNode
{
    public IReadOnlyList<SlotNode> Items { get; init; } = [];

    protected override IEnumerable<SlotNode> Children => Items;
}

/// <summary>
/// A function and the sequence of each of its arguments. The box is a container: what is written in
/// it are the arguments, and what is not written yet is the ghost place at the end, which the call
/// always has whether or not the text says so.
/// </summary>
public sealed class SlotCall : SlotNode
{
    public required string Name { get; init; }

    /// <summary>Which family of functions it belongs to, which is the colour its box is drawn in.</summary>
    public required string Tone { get; init; }

    public IReadOnlyList<SlotSequence> Arguments { get; init; } = [];

    /// <summary>
    /// The place at the end that is not in the text: the room a call has left over, drawn as a
    /// dashed plus. It never counts as an argument, so the formula does not grow until something is
    /// actually put there. Null when the function takes no more arguments than are already written.
    /// </summary>
    public SlotEmpty? Ghost { get; init; }

    /// <summary>True for a call written with nothing in it, which the box says in words.</summary>
    public bool HasNoArguments => Arguments.Count == 0;

    /// <summary>
    /// True when another empty place can be written in front of the ghost. With nothing written
    /// there is nothing to put a comma after, and the ghost is already the one place to fill.
    /// </summary>
    public bool CanAddPlace => Ghost is not null && Arguments.Count > 0;

    protected override IEnumerable<SlotNode> Children => Arguments;
}

/// <summary>The sign between two pieces, as a piece of its own — spaces and all.</summary>
public sealed class SlotOperator : SlotNode
{
    /// <summary>How the sign reads, without the spaces around it.</summary>
    public required string Text { get; init; }
}

/// <summary>
/// One bracket character as a piece of its own. A bracket is a token like any other here: it can be
/// clicked, swapped, dragged and taken away with its own cross, so a pair that was written by hand
/// can be pulled apart and put back around something else.
/// </summary>
public sealed class SlotBracket : SlotNode
{
    public required string Text { get; init; }
}

/// <summary>A name, a number, a piece of text or one of the words that stand for a value.</summary>
public sealed class SlotValue : SlotNode
{
    public required string Text { get; init; }

    public required string Tone { get; init; }
}

/// <summary>
/// A place the formula needs but does not have: an argument left out, an empty place between two
/// commas, or the ghost at the end of a call. It says what belongs there, and a click on it puts the
/// caret exactly where that thing would go.
/// </summary>
public sealed class SlotEmpty : SlotNode
{
    public required string Text { get; init; }

    /// <summary>What this place is for, in one line, shown on hover. Empty when the text says it.</summary>
    public string Hint { get; init; } = string.Empty;

    /// <summary>
    /// True for the place at the end of a call that is not written anywhere. A ghost is not an
    /// argument: it takes no part in the formula, and filling it writes the comma it stands behind.
    /// </summary>
    public bool IsGhost { get; init; }

    /// <summary>
    /// True while the call still wants a value here — the red dashed box. False for the ghost of a
    /// call that already has everything it asks for, which is simply room for one more.
    /// </summary>
    public bool Required { get; init; }
}

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
