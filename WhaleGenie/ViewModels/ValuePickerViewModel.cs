using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

// The language itself, spelled out: the property below is the text being edited, which hides the
// static class of the same name inside this file.
using Language = WhaleGenie.Core.Expressions.Expression;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One of the plain values a formula can be made of: what the button says, what it inserts, and how
/// far back the caret goes so a pair of quotes opens between its two halves.
/// </summary>
public sealed record ValueToken(string Text, string Insert, int Back = 0);

/// <summary>
/// Backs the window that fills a cell. Composing a formula needs its materials in sight at once —
/// the signs, the names and the functions — so they stand in three panels on one page rather than
/// behind tabs: switching pages to reach the next piece of a formula is the cost this window is
/// built to remove. The formula itself is the one line everything writes into, and it is also the
/// line that can be typed in directly.
/// </summary>
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    private readonly List<ExpressionFunctionChoice> _functions;

    /// <summary>
    /// The function table keyed by the name a formula is written with, which is what the structure
    /// band asks while it reads: whether a name is a function, which family it belongs to, and how
    /// many places it needs.
    /// </summary>
    private readonly Dictionary<string, ExpressionFunction> _table;

    private bool _reads;

    public ValuePickerViewModel(string? seed, IReadOnlyList<VariableChoice>? variables, bool namesOnly,
        string? target = null)
    {
        NamesOnly = namesOnly;
        Target = target ?? string.Empty;

        // The function table is what the structure band reads a line with, so it is in hand before
        // the line is set: setting the text is what asks the band to read it.
        _functions = [.. Language.Functions.Select(function => new ExpressionFunctionChoice(function))];
        _table = Language.Functions.ToDictionary(function => function.Name, StringComparer.OrdinalIgnoreCase);

        Text = seed ?? string.Empty;

        // The wall keeps what a flat list cannot: the steps that read a name, and the way to one.
        // Its own search and filters are shown, because the variables panel is that wall.
        Wall = new VariableWallViewModel(variables) { ShowsDetails = false };
        Wall.Jumped += step => Jumped?.Invoke(step);
        Wall.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VariableWallViewModel.Looking))
            {
                // One material is being described at a time: picking a name lets go of the card
                // that was looked at before, and the other way round.
                if (Wall.Looking is not null)
                {
                    SelectedFunction = null;
                }

                OnPropertyChanged(nameof(HasChosenVariable));
                OnPropertyChanged(nameof(CanConfirm));
            }
        };

        Operators = SplitBrackets(OperatorPalette.Build());
        // A number is typed, and text has its own sign in the operator panel; what is left is the
        // three words a formula cannot be typed from memory as easily as it can be clicked.
        Values =
        [
            new ValueToken(Strings.Get("ExprBuilder.Value.True"), "true"),
            new ValueToken(Strings.Get("ExprBuilder.Value.False"), "false"),
            new ValueToken(Strings.Get("ExprBuilder.Value.Null"), "null"),
        ];

        FunctionGroups = BuildGroups();
        SelectedFunctionGroup = FunctionGroups[0];
        RefreshFunctions();
        CheckText();
    }

    /// <summary>Raised with what the cell should hold, or null when the window was left alone.</summary>
    public event Action<string?>? CloseRequested;

    /// <summary>Raised with a step somebody asked to be shown, so the editor behind can look.</summary>
    public event Action<MacroStep>? Jumped;

    public VariableWallViewModel Wall { get; }

    /// <summary>The signs a formula is built from, written as the engine reads them.</summary>
    public IReadOnlyList<ExpressionOperator> Operators { get; }

    /// <summary>The plain words that are worth a button rather than a memory.</summary>
    public IReadOnlyList<ValueToken> Values { get; }

    /// <summary>
    /// True where the cell only ever holds a name — the name a variable is written under, or the
    /// selector a step reads. A formula there would be stored as text and never read as one, so the
    /// window does not draw the formula or the two panels that build one.
    /// </summary>
    public bool NamesOnly { get; }

    public bool CanCompute => !NamesOnly;

    public string Title => NamesOnly ? Strings.Get("ValuePicker.Names") : Strings.Get("ValuePicker.Title");

    /// <summary>Which cell the window was opened for. Empty when nobody said.</summary>
    public string Target { get; }

    public string TargetLine => Target.Length > 0
        ? Strings.Format("ValuePicker.Target", Target)
        : string.Empty;

    public bool HasTarget => TargetLine.Length > 0;

    /// <summary>What the cell holds now, and what will go back into it.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>One line under the formula: whether it reads, or why it does not.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NoteIsProblem { get; set; }

    /// <summary>The formula said in words, for a reader who does not follow the punctuation.</summary>
    [ObservableProperty]
    public partial string Saying { get; set; } = string.Empty;

    /// <summary>True when the formula reads, which is what lets it be handed back.</summary>
    public bool Reads => _reads;

    public bool HasSaying => Saying.Length > 0;

    /// <summary>What is typed into the function panel's search box.</summary>
    [ObservableProperty]
    public partial string FunctionQuery { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFunctions))]
    public partial ExpressionFunctionFilter? SelectedFunctionGroup { get; set; }

    public IReadOnlyList<ExpressionFunctionFilter> FunctionGroups { get; }

    public bool IsAllFunctions => SelectedFunctionGroup is { Group.Length: 0 };

    public ObservableCollection<ExpressionFunctionChoice> Functions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenFunction))]
    public partial ExpressionFunctionChoice? SelectedFunction { get; set; }

    public bool HasChosenFunction => SelectedFunction is not null;

    public bool HasChosenVariable => Wall.Looking is not null;

    public bool IsFunctionsEmpty => Functions.Count == 0;

    /// <summary>
    /// The formula as it is nested: a call holds the pieces written inside its brackets, and a piece
    /// that is itself a call holds its own. The band draws this shape, so the brackets on the page and
    /// the brackets in the formula are the same brackets.
    /// </summary>
    [ObservableProperty]
    public partial SlotNode? Recipe { get; set; }

    public bool HasRecipe => Recipe is not null;

    public bool CanConfirm => NamesOnly ? Wall.Looking is not null : _reads;

    /// <summary>One short line next to the confirm button saying why it is grey.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockReason))]
    [NotifyPropertyChangedFor(nameof(BlockReasonTip))]
    public partial string BlockReason { get; set; } = string.Empty;

    public bool HasBlockReason => BlockReason.Length > 0;

    /// <summary>Null when there is no reason, so the confirm button opens no wordless tooltip.</summary>
    public string? BlockReasonTip => HasBlockReason ? BlockReason : null;

    private readonly Stack<string> _before = new();
    private readonly Stack<string> _after = new();
    private string? _last;
    private bool _replaying;

    public bool CanUndo => _before.Count > 0;

    public bool CanRedo => _after.Count > 0;

    /// <summary>
    /// Goes back one step. Every way of changing the line — typing, a piece from the strip, a cross,
    /// a drag — writes the text, so one place is enough to remember all of them.
    /// </summary>
    public void Undo()
    {
        if (_before.Count == 0)
        {
            return;
        }

        _after.Push(Text);
        Replay(_before.Pop());
    }

    /// <summary>Goes forward again to the line undo left behind.</summary>
    public void Redo()
    {
        if (_after.Count == 0)
        {
            return;
        }

        _before.Push(Text);
        Replay(_after.Pop());
    }

    private void Replay(string text)
    {
        _replaying = true;
        Text = text;
        _replaying = false;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void Remember(string previous)
    {
        if (_replaying || previous == Text)
        {
            return;
        }

        _before.Push(previous);
        _after.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>
    /// True while the strip is asking for a number or a piece of text. Those two are the only things
    /// a formula holds that no card can offer — a number is whatever the person needs, and a piece
    /// of text is whatever they mean — so they are typed once, here, and never in the formula box.
    /// </summary>
    [ObservableProperty]
    public partial bool IsTypingValue { get; set; }

    /// <summary>True when what is being typed is a number, false when it is a piece of text.</summary>
    [ObservableProperty]
    public partial bool IsTypingNumber { get; set; }

    /// <summary>What the strip is asking for, in the reader's language.</summary>
    [ObservableProperty]
    public partial string TypingLabel { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string TypingText { get; set; } = string.Empty;

    /// <summary>Why what is typed cannot be put in, or empty while it can.</summary>
    [ObservableProperty]
    public partial string TypingNote { get; set; } = string.Empty;

    public bool HasTypingNote => TypingNote.Length > 0;

    public bool CanInsertTyped => TypedToken() is not null;

    partial void OnTypingTextChanged(string value) => RefreshTyped();

    /// <summary>Opens the one-line asking row for a number or for a piece of text.</summary>
    public void BeginTyping(bool number)
    {
        IsTypingNumber = number;
        TypingLabel = Strings.Get(number ? "ValuePicker.Value.Number" : "ValuePicker.Value.Text");
        TypingText = string.Empty;
        IsTypingValue = true;
        RefreshTyped();
    }

    /// <summary>Closes the asking row and forgets what was typed into it.</summary>
    public void EndTyping()
    {
        IsTypingValue = false;
        TypingText = string.Empty;
        TypingNote = string.Empty;
    }

    /// <summary>
    /// What the strip would put into the formula, or null while what is typed is not something the
    /// language holds. Text goes in quoted, with the quotes and backslashes it contains escaped the
    /// way the language reads them back.
    /// </summary>
    public string? TypedToken()
    {
        if (!IsTypingNumber)
        {
            return Quoted(TypingText);
        }

        var written = TypingText.Trim();
        return written.Length > 0
               && double.TryParse(written, NumberStyles.Any, CultureInfo.InvariantCulture, out _)
            ? written
            : null;
    }

    private void RefreshTyped()
    {
        TypingNote = IsTypingNumber && TypingText.Trim().Length > 0 && TypedToken() is null
            ? Strings.Get("ValuePicker.Value.BadNumber")
            : string.Empty;

        OnPropertyChanged(nameof(CanInsertTyped));
        OnPropertyChanged(nameof(HasTypingNote));
    }

    /// <summary>The text as the language reads it back: quoted, with what is inside it escaped.</summary>
    private static string Quoted(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    partial void OnTextChanged(string value)
    {
        if (_last is { } previous)
        {
            Remember(previous);
        }

        _last = value;
        CheckText();
    }

    partial void OnFunctionQueryChanged(string value) => RefreshFunctions();

    partial void OnSelectedFunctionGroupChanged(ExpressionFunctionFilter? value) => RefreshFunctions();

    partial void OnSelectedFunctionChanged(ExpressionFunctionChoice? value)
    {
        if (value is not null)
        {
            Wall.Looking = null;
        }
    }

    /// <summary>
    /// Brackets come into the band one character at a time: a pair is two pieces that can each be
    /// chosen, swapped, dragged or taken away. The old window still offers them as a pair.
    /// </summary>
    private static IReadOnlyList<ExpressionOperator> SplitBrackets(IReadOnlyList<ExpressionOperator> all)
    {
        var split = new List<ExpressionOperator>();
        foreach (var one in all)
        {
            switch (one.Insert)
            {
                case "()":
                    split.Add(new ExpressionOperator("(", "(", "Expr.Op.Group"));
                    split.Add(new ExpressionOperator(")", ")", "Expr.Op.Group"));
                    break;
                case "[]":
                    split.Add(new ExpressionOperator("[", "[", "Expr.Op.List"));
                    split.Add(new ExpressionOperator("]", "]", "Expr.Op.List"));
                    break;
                // 引号不再摆在符号条上：它是"一段文字"的边界，不是可以随手插一个的符号。
                case "\"\"":
                    break;
                default:
                    split.Add(one);
                    break;
            }
        }

        return split;
    }

    /// <summary>Narrows the function panel to the group and the word typed into it.</summary>
    public void RefreshFunctions()
    {
        var query = FunctionQuery.Trim();
        var group = SelectedFunctionGroup?.Group ?? string.Empty;

        Functions.Clear();
        foreach (var function in _functions)
        {
            if (group.Length > 0 && function.Group != group)
            {
                continue;
            }

            if (query.Length > 0 && !Matches(function, query))
            {
                continue;
            }

            Functions.Add(function);
        }

        OnPropertyChanged(nameof(IsFunctionsEmpty));

        // The row that is no longer in the list cannot stay chosen, and a list with nothing chosen
        // cannot say what a card means.
        if (SelectedFunction is not null && !Functions.Contains(SelectedFunction))
        {
            SelectedFunction = null;
        }
    }

    /// <summary>One stretch of the formula, measured in the coordinates of the text it came from.</summary>
    public readonly record struct CutSpan(int Start, int End)
    {
        public int Length => End - Start;
    }

    /// <summary>
    /// What a drop would do, which is everything the band needs to draw the cursor. The window draws
    /// from this and applies the same one on release, so the picture and the result cannot disagree.
    /// </summary>
    public readonly record struct DropPreview(
        SlotNode Anchor, bool After, bool Allowed, bool NoOp);

    /// <summary>
    /// Which side of a piece a pointer is on. A run of pieces reads top to bottom when it folds, so
    /// there the vertical half says before or after; a single piece still reads left to right.
    /// </summary>
    public static bool AfterFor(SlotNode? target, bool rightHalf, bool lowerHalf)
        => target is SlotSequence or SlotCall { Stacked: true } ? lowerHalf : rightHalf;

    /// <summary>A move worked out before anything is cut: where, what, and what goes around it.</summary>
    private readonly record struct MovePlan(SlotNode Piece, int Seam, string Raw, string Lead, string Trail);

    /// <summary>
    /// Adds one material to the line. Where it lands is decided by the insertion caret; until that
    /// is wired up the end of the line is the only place, which is where a formula built one piece
    /// at a time has got to. Nothing already written is ever taken over.
    /// </summary>
    public void Place(string token) => Append(token);

    /// <summary>Adds an empty call without putting it around anything already written.</summary>
    public void PlaceCall(string name) => Append(name + "()");

    /// <summary>
    /// Puts one space where the characters now touching would be read as a single piece: two numbers
    /// becoming one number, a name growing a letter, two strings running together. Everything else
    /// — <c>-</c> in front of a number, a bracket beside a word — is left alone.
    /// </summary>
    private void JoinSeam(int at, string written)
    {
        if (written.Length == 0 || at < 0)
        {
            return;
        }

        var lead = at > 0 && at <= Text.Length && Glues(Text[at - 1], written[0]);

        var tailAt = at + written.Length;
        var tail = tailAt < Text.Length && Glues(written[^1], Text[tailAt]);

        if (!lead && !tail)
        {
            return;
        }

        var text = lead ? Text.Insert(at, " ") : Text;
        if (tail)
        {
            text = text.Insert(tailAt + (lead ? 1 : 0), " ");
        }

        Text = text;
    }

    /// <summary>True for the characters a name, a number or a word is made of.</summary>
    private static bool IsWord(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '.' or '$';

    /// <summary>
    /// True when these two characters would be read as one piece if they ended up side by side.
    /// </summary>
    private static bool Glues(char left, char right)
        => (IsWord(left) && IsWord(right))
            || left is '"' or '\''
            || right is '"' or '\''
            || (left, right) is ('!', '=') or ('<', '=') or ('>', '=') or ('=', '=') or ('&', '&') or ('|', '|');

    /// <summary>
    /// Writes a piece at the end of the line, with one space in front of it so two pieces never run
    /// into each other.
    /// </summary>
    private void Append(string token)
    {
        var at = Text.TrimEnd().Length;
        var lead = at == 0 ? string.Empty : " ";
        Text = Text.Insert(at, lead + token);
    }

    /// <summary>
    /// Takes one piece away, and the comma that kept it apart from its neighbour. The ghost at the
    /// end needs no restoring: it is not stored anywhere, it is what a call always has, so it is
    /// there again the moment the text is read back.
    /// </summary>
    public bool RemoveArgument(SlotNode? piece)
    {
        if (piece is not { CanRemove: true, Length: > 0 } target)
        {
            return false;
        }

        Cut(target.Start, target.Length);
        return true;
    }

    /// <summary>
    /// Where a piece would land if it were dropped here. The band draws one cursor from this and
    /// applies the same thing on release, so what the person sees cannot disagree with what happens.
    /// </summary>
    public DropPreview PreviewMove(SlotNode? piece, SlotNode? target, bool after)
    {
        if (!Resolve(piece, target, after, out var anchor, out var dropAfter, out _))
        {
            return new DropPreview((target ?? piece ?? Recipe)!, after, Allowed: false, NoOp: false);
        }

        var seam = SeamOf(anchor, dropAfter);
        var noOp = piece is { } moved && Inside(seam, moved);
        return new DropPreview(anchor, dropAfter, Allowed: !noOp, noOp);
    }

    /// <summary>
    /// Moves one piece to just before (or after) another. The piece is taken out the same way its
    /// cross takes it out — commas and all — and written back where it was dropped, carrying the
    /// comma that keeps it apart from its neighbour inside a call, and a space where it would
    /// otherwise fuse with what stands next to it.
    /// </summary>
    public bool MoveOnto(SlotNode? piece, SlotNode? target, bool after)
    {
        if (!TryPlanMove(piece, target, after, out var plan))
        {
            return false;
        }

        var span = Cut(plan.Piece.Start, plan.Piece.Length);
        var at = Math.Clamp(SeamAfterCut(plan.Seam, span), 0, Text.Length);

        var lead = plan.Lead;
        var trail = plan.Trail;
        if (lead.Length == 0 && at > 0 && Glues(Text[at - 1], plan.Raw[0]))
        {
            lead = " ";
        }

        if (trail.Length == 0 && at < Text.Length && Glues(plan.Raw[^1], Text[at]))
        {
            trail = " ";
        }

        // A move never means to leave spaces hanging off either end of the line.
        Text = Text.Insert(at, lead + plan.Raw + trail).Trim();
        return true;
    }

    /// <summary>
    /// Works out what a move would do without doing it: the seam, counted in the text as it stands
    /// now, and the separators that seam asks for. Everything is measured before the cut, because
    /// cutting can take away the very comma the seam was measured against.
    /// </summary>
    private bool TryPlanMove(SlotNode? piece, SlotNode? target, bool after, out MovePlan plan)
    {
        plan = default;
        if (piece is not { CanRemove: true, Length: > 0 } moved
            || !Resolve(piece, target, after, out var anchor, out var dropAfter, out var gap))
        {
            return false;
        }

        var raw = Text.Substring(moved.Start, moved.Length).Trim();
        var seam = SeamOf(anchor, dropAfter);

        // Dropping a piece where it already stands is not a move: it would only cut and paste the
        // same characters back, sometimes leaving an empty argument behind.
        if (raw.Length == 0 || Inside(seam, moved))
        {
            return false;
        }

        var (lead, trail) = SeparatorsFor(anchor, seam, gap);
        plan = new MovePlan(moved, seam, raw, lead, trail);
        return true;
    }

    /// <summary>
    /// Decides where a drop actually goes. A run of pieces is not a thing to drop beside — the gap
    /// between two of them belongs to the run, so dropping in that gap means the end of the run.
    /// </summary>
    private bool Resolve(
        SlotNode? piece,
        SlotNode? target,
        bool after,
        out SlotNode anchor,
        out bool dropAfter,
        out bool gap)
    {
        anchor = target ?? piece ?? Recipe!;
        dropAfter = after;
        gap = false;

        if (piece is not { CanRemove: true, Length: > 0 } moved
            || target is null
            || ReferenceEquals(moved, target))
        {
            return false;
        }

        // A run is not a thing to drop beside: the gap between its pieces is the run's own space,
        // so a drop there belongs at the end of it. Checked before the "inside the piece" test,
        // because a run starts where its first piece does.
        if (target is SlotSequence run)
        {
            if (run.Items.LastOrDefault(one => one.Length > 0) is not { } last
                || ReferenceEquals(last, moved))
            {
                return false;
            }

            anchor = last;
            dropAfter = true;
            gap = true;
            return true;
        }

        return target.Start < moved.Start || target.Start >= moved.Start + moved.Length;
    }

    /// <summary>
    /// True when a seam is where this piece already stands — its own characters, or the space and
    /// comma that keep it from its neighbour. Dropping it there would take it out and put it back a
    /// little to the left, which is how "f(a, b, c)" used to become "f(ab, , c)".
    /// </summary>
    private bool Inside(int seam, SlotNode piece)
    {
        var start = piece.Start;
        while (start > 0 && Text[start - 1] == ' ')
        {
            start--;
        }

        if (start > 0 && Text[start - 1] == ',')
        {
            start--;
        }

        var end = piece.Start + piece.Length;
        while (end < Text.Length && Text[end] == ' ')
        {
            end++;
        }

        return seam >= start && seam <= end;
    }

    /// <summary>Where a drop lands, counted in the text as it stands before anything is cut.</summary>
    private static int SeamOf(SlotNode anchor, bool after) => after ? anchor.Start + anchor.Length : anchor.Start;

    /// <summary>
    /// The text that has to be written around a piece at this seam: inside a call's arguments it is
    /// the comma that keeps it apart from its neighbour — in front at the end of the list, behind it
    /// anywhere else. In front of a ghost it is the comma the ghost was standing behind. Everywhere
    /// else nothing, and the seam itself decides whether a space is needed.
    /// </summary>
    private (string Lead, string Trail) SeparatorsFor(SlotNode anchor, int seam, bool gap)
    {
        // Dropped into the space between pieces: it joins the end of that run, one space along.
        if (gap)
        {
            return (seam > 0 ? " " : string.Empty, string.Empty);
        }

        if (anchor is SlotEmpty { IsGhost: true } ghost)
        {
            return (ghost.Before, string.Empty);
        }

        if (anchor is SlotEmpty)
        {
            return (string.Empty, string.Empty);
        }

        var call = Recipe?.Walk()
            .OfType<SlotCall>()
            .Where(one => one.Arguments.Count > 0)
            .OrderBy(one => one.Length)
            .FirstOrDefault(one =>
            {
                var first = one.Arguments[0].Start;
                var last = one.Ghost?.Start ?? one.Start + one.Length;
                return seam >= first && seam <= last;
            });

        if (call is null)
        {
            return (string.Empty, string.Empty);
        }

        var end = call.Ghost?.Start ?? call.Start + call.Length;
        return seam >= end ? (", ", string.Empty) : (string.Empty, ", ");
    }

    /// <summary>Maps a seam measured before a cut onto the text that is left after it.</summary>
    public static int SeamAfterCut(int seam, CutSpan span)
        => seam >= span.End ? seam - span.Length
            : seam > span.Start ? span.Start
            : seam;

    /// <summary>
    /// Takes a span out and tidies the seam: the comma that kept the piece apart from its neighbours
    /// goes with it, and two spaces never end up next to each other. One rule covers everything that
    /// can be removed — an argument, a sign, a bracket — because all of them are just spans.
    /// </summary>
    private CutSpan Cut(int start, int length)
    {
        var original = Text;
        var at = Math.Clamp(start, 0, Text.Length);
        var end = Math.Clamp(at + length, at, Text.Length);
        var text = Text.Remove(at, end - at);

        var before = at - 1;
        while (before >= 0 && text[before] == ' ')
        {
            before--;
        }

        var after = at;
        while (after < text.Length && text[after] == ' ')
        {
            after++;
        }

        var left = before >= 0 && text[before] == ',';
        var right = after < text.Length && (text[after] == ',' || text[after] is ')' or ']');

        if (left && right)
        {
            // The comma in front kept it from its neighbour; that neighbour now keeps the next one.
            var from = before;
            while (from > 0 && text[from - 1] == ' ')
            {
                from--;
            }

            text = text.Remove(from, at - from);
        }
        else if (right && text[after] == ',')
        {
            var to = after + 1;
            while (to < text.Length && text[to] == ' ')
            {
                to++;
            }

            text = text.Remove(at, to - at);
        }
        if (at > 0 && at < text.Length && text[at - 1] == ' ' && text[at] == ' ')
        {
            text = text.Remove(at, 1);
        }

        // Taking a sign out from between two numbers would leave one number where there were two.
        if (at > 0 && at < text.Length && Glues(text[at - 1], text[at]))
        {
            text = text.Insert(at, " ");
        }

        Text = text;
        return Difference(original, Text);
    }

    /// <summary>
    /// The stretch of text that actually left, found by comparing what was written with what is
    /// written now. Tidying up around a cut takes commas and spaces away on its own, so counting the
    /// characters beforehand would put the seam in the wrong place.
    /// </summary>
    private static CutSpan Difference(string before, string after)
    {
        var head = 0;
        var shortest = Math.Min(before.Length, after.Length);
        while (head < shortest && before[head] == after[head])
        {
            head++;
        }

        var tail = 0;
        while (tail < shortest - head
               && before[before.Length - 1 - tail] == after[after.Length - 1 - tail])
        {
            tail++;
        }

        return new CutSpan(head, before.Length - tail);
    }

    /// <summary>
    /// Writes another empty place into the call. The ghost cannot become several — it is the place
    /// that is not written — so the only way to have two places to fill at once is to write one.
    /// </summary>
    public bool AppendArgument(SlotCall? call)
    {
        if (call is not { CanAddPlace: true, Ghost: { } ghost })
        {
            return false;
        }

        var at = Math.Clamp(ghost.Start, 0, Text.Length);
        Text = Text.Insert(at, ghost.Before);
        return true;
    }

    /// <summary>Hands back what the cell should hold: the line, or the one name it wanted.</summary>
    public void Confirm()
    {
        if (NamesOnly)
        {
            if (Wall.Looking is { } chosen)
            {
                CloseRequested?.Invoke(chosen.Token);
            }

            return;
        }

        if (_reads)
        {
            CloseRequested?.Invoke(Text.Trim());
        }
    }

    /// <summary>Hands back one name, for a cell that holds nothing else.</summary>
    public void Choose(string token) => CloseRequested?.Invoke(token);

    public void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>Why the formula does not read, or that it does. Empty is not a mistake yet.</summary>
    private void CheckText()
    {
        if (NamesOnly)
        {
            return;
        }

        var written = Text.Trim();
        ExpressionException? problem = null;
        if (written.Length == 0)
        {
            _reads = false;
            NoteIsProblem = false;
            Note = Strings.Get("ValuePicker.Note.Empty");
            Saying = string.Empty;
        }
        else if (Language.TryParse(written, out var parsed, out var error))
        {
            _reads = true;
            NoteIsProblem = false;
            Note = Strings.Get("ValuePicker.Note.Reads");
            Saying = ExpressionText.InChinese
                ? Strings.Get("ValuePicker.ReadsAs") + ExpressionPhrase.Say(parsed!, VariableCatalog.Spoken)
                : string.Empty;
        }
        else
        {
            _reads = false;
            NoteIsProblem = true;
            problem = error;
            Note = error is null
                ? Strings.Get("ExprBuilder.CannotRead")
                : ExpressionText.Describe(error);
            Saying = string.Empty;
        }

        RebuildRecipe();
        SlotDiagnostics.Mark(Recipe, problem, _table);
        BlockReason = written.Length == 0
            ? Strings.Get("ValuePicker.Confirm.Empty")
            : _reads ? string.Empty : Strings.Get("ValuePicker.Confirm.Unreadable");

        OnPropertyChanged(nameof(Reads));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(HasSaying));
    }

    /// <summary>
    /// Reads the line into the shape the band draws. An empty formula is one empty place rather than
    /// nothing at all: the page says "a value goes here" instead of looking broken.
    /// </summary>
    private void RebuildRecipe()
    {
        Recipe = NamesOnly ? null : SlotReader.Read(Text, _table);
        RefreshStacking(SlotStacking.CompactWidthPx);
        OnPropertyChanged(nameof(HasRecipe));
    }

    /// <summary>
    /// Says which calls need one row per argument. The band gives it the width it actually has, so
    /// narrowing the window can fold a call that fitted on a wide one.
    /// </summary>
    public void RefreshStacking(double availableWidth)
    {
        if (Recipe is null)
        {
            return;
        }

        // Before the first layout pass the viewport has no width; the compact estimate is the stand
        // in until the band can say how much room it really has.
        var width = availableWidth > 0 ? availableWidth : SlotStacking.CompactWidthPx;
        Fold(Recipe, width, insideStacked: false);
    }

    /// <summary>
    /// One level folds at a time. A call already drawn one row per argument gives its nested calls
    /// a whole row each to lie on; folding those too would multiply the height and push the rest of
    /// the formula out of the scrollable strip.
    /// </summary>
    private static void Fold(SlotNode node, double width, bool insideStacked)
    {
        if (node is SlotCall call)
        {
            var stacked = !insideStacked && SlotStacking.Decide(call, width);
            call.Stacked = stacked;
            foreach (var argument in call.Arguments)
            {
                Fold(argument, width, insideStacked || stacked);
            }

            return;
        }

        if (node is SlotSequence sequence)
        {
            foreach (var piece in sequence.Items)
            {
                Fold(piece, width, insideStacked);
            }
        }
    }

    /// <summary>A label written from the text table, read again when the language changes.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        RebuildRecipe();
    }

    /// <summary>True when what is typed names this function, its call, or what it does.</summary>
    private static bool Matches(ExpressionFunctionChoice function, string query)
        => function.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Signature.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Heading.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.ChineseSignature.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Hint.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>All of the language, then each group once, in the order the language states them.</summary>
    private IReadOnlyList<ExpressionFunctionFilter> BuildGroups()
    {
        var groups = new List<ExpressionFunctionFilter> { new(string.Empty, "ExprBuilder.FunctionsAll") };
        foreach (var function in _functions)
        {
            if (groups.All(filter => filter.Group != function.Group))
            {
                groups.Add(new ExpressionFunctionFilter(function.Group, $"Expr.Group.{function.Group}"));
            }
        }

        return groups;
    }
}
