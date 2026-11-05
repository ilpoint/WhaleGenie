using System;
using System.Collections.Generic;
using System.Linq;
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
    /// True when this piece wears a cross. The cross belongs to the piece under the pointer and to
    /// nothing else: with one on every piece at once the line is a thicket of them.
    /// </summary>
    public bool ShowsRemove => CanRemove && IsHovered;

    /// <summary>True on the piece a check is complaining about, which draws a red edge around it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoverHint))]
    public partial bool HasProblem { get; set; }

    /// <summary>One line saying what is wrong here, shown while the pointer rests on the piece.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HoverHint))]
    public partial string ProblemHint { get; set; } = string.Empty;

    /// <summary>
    /// What the pointer bubble says: a complaint first, then what the place is for. Null when there
    /// is nothing to say: a tooltip bound to an empty string still opens a bordered but wordless
    /// bubble, which is worse than no bubble at all.
    /// </summary>
    public string? HoverHint => HasProblem
        ? (ProblemHint.Length > 0 ? ProblemHint : null)
        : PlaceHint;

    /// <summary>What the place is for when nothing is wrong with it.</summary>
    protected virtual string? PlaceHint => null;

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

    /// <summary>What the argument written in this run is called. Empty on a run that is not an argument.</summary>
    public string ArgumentLabel { get; set; } = string.Empty;

    /// <summary>One word saying whether the argument is required or optional.</summary>
    public string ArgumentFlag { get; set; } = string.Empty;

    /// <summary>Where this argument stands in its call, counted from one.</summary>
    public int ArgumentIndex { get; set; }

    public bool ArgumentOptional { get; set; }

    public bool ArgumentRequired { get; set; }

    public bool HasArgumentLabel => ArgumentLabel.Length > 0;

    public bool HasArgumentFlag => ArgumentFlag.Length > 0;

    protected override IEnumerable<SlotNode> Children => Items;
}

/// <summary>
/// A function and the sequence of each of its arguments. The box is a container: what is written in
/// it are the arguments, and what is not written yet is the ghost place at the end, which the call
/// always has whether or not the text says so.
/// </summary>
public sealed partial class SlotCall : SlotNode
{
    public required string Name { get; init; }

    /// <summary>True when its arguments are drawn one per row rather than in one wrapping line.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Compact))]
    public partial bool Stacked { get; set; }

    /// <summary>The other side of <see cref="Stacked"/>, for templates that only need a visibility.</summary>
    public bool Compact => !Stacked;

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

    protected override string? PlaceHint => Hint.Length > 0 ? Hint : null;
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

/// <summary>
/// Points a complaint at the piece that caused it. The engine says what went wrong but not where a
/// character stands, so the band matches the offending name against its own pieces and checks the
/// arguments against the same function table the language uses. Nothing here writes the formula.
/// </summary>
internal static class SlotDiagnostics
{
    public static void Mark(SlotNode? recipe, ExpressionException? error,
        IReadOnlyDictionary<string, ExpressionFunction> table)
    {
        if (recipe is null)
        {
            return;
        }

        foreach (var piece in recipe.Walk())
        {
            piece.HasProblem = false;
            piece.ProblemHint = string.Empty;
        }

        if (error is not null)
        {
            var described = ExpressionText.Describe(error);
            if (error.Code is ExpressionErrorCode.UnknownFunction or ExpressionErrorCode.ArgumentCount)
            {
                var calls = recipe.Walk().OfType<SlotCall>().ToList();
                var named = calls.FirstOrDefault(call =>
                    error.Detail.Equals(call.Name, StringComparison.OrdinalIgnoreCase)
                    || error.Detail.StartsWith(call.Name + "(", StringComparison.OrdinalIgnoreCase));
                Problem(named ?? calls.LastOrDefault() ?? (SlotNode)recipe, described);
            }
            else
            {
                Problem(recipe, described);
            }
        }

        foreach (var call in recipe.Walk().OfType<SlotCall>())
        {
            if (!table.TryGetValue(call.Name, out var entry))
            {
                continue;
            }

            if (call.Arguments.Count < entry.MinArguments)
            {
                var place = call.Ghost
                    ?? call.Arguments.LastOrDefault()?.Items.OfType<SlotEmpty>().FirstOrDefault();
                if (place is not null)
                {
                    Problem(place, Strings.Get("ValuePicker.Problem.MissingArgument"));
                }
            }

            if (entry.MaxArguments < call.Arguments.Count)
            {
                Problem(call.Arguments[entry.MaxArguments],
                    Strings.Get("ValuePicker.Problem.ExtraArgument"));
            }
        }
    }

    private static void Problem(SlotNode piece, string hint)
    {
        piece.HasProblem = true;
        piece.ProblemHint = hint;
    }
}

/// <summary>
/// Decides when a call is too wide to stay on one line. A short call keeps the compact row; a long
/// one gets a row per argument. Widths are estimates from the text, not measured pixels: this has
/// to say the same thing in a headless test as it does on a wide screen.
/// </summary>
internal static class SlotStacking
{
    /// <summary>More arguments than this always get their own rows.</summary>
    public const int ArgumentThreshold = 3;

    /// <summary>One character of the band's monospace face, in device independent pixels.</summary>
    public const double CharWidthPx = 8;

    /// <summary>What one token adds on top of its letters: padding, margins and a rounded edge.</summary>
    public const double TokenOverheadPx = 26;

    /// <summary>The width a compact call is compared with when no window has measured one yet.</summary>
    public const double CompactWidthPx = 560;

    public static bool Decide(SlotCall call, double availableWidth)
        => call.Arguments.Count > ArgumentThreshold
            || call.Arguments.Any(argument => argument.Items.Any(piece => piece is SlotCall))
            || EstimateWidth(call) > Math.Min(availableWidth, CompactWidthPx);

    public static double EstimateWidth(SlotCall call)
    {
        var width = (call.Name.Length + 2) * CharWidthPx + TokenOverheadPx;
        foreach (var argument in call.Arguments)
        {
            width += 2 * CharWidthPx;
            foreach (var piece in argument.Items)
            {
                width += WrittenWidth(piece);
            }
        }

        return width;
    }

    private static double WrittenWidth(SlotNode piece) => piece switch
    {
        SlotCall nested => EstimateWidth(nested),
        _ => Math.Max(1, piece.Length) * CharWidthPx + TokenOverheadPx,
    };
}
