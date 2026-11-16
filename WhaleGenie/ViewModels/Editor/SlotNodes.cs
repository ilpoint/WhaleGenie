using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.ViewModels;

// The pieces a formula is made of, and how they are walked.
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
    /// <summary>
    /// Where this piece begins in the drawn shape. A shape has no characters to count, so this is
    /// the stable order the band draws its pieces in: a caret, a cross and a drop all name a place
    /// by the piece's <see cref="Path"/>, and the offset is only what the geometry measures.
    /// </summary>
    public int Start { get; set; }

    /// <summary>How much of that order it covers. An empty place covers none.</summary>
    public int Length { get; set; }

    /// <summary>
    /// The walk from the value's root to the shape this piece draws. A piece built from a shape
    /// tree knows where it stands in that tree, so a caret, a removal and a drop all name the same
    /// place without measuring characters in a line. Null on a piece read from a line of text.
    /// </summary>
    public NodePath? Path { get; init; }

    /// <summary>The shape this piece stands for, or null for a place that holds nothing yet.</summary>
    public ExpressionNode? Source { get; init; }

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
    public required string Text { get; set; }
}

/// <summary>A name, a number, a piece of text or one of the words that stand for a value.</summary>
public sealed class SlotValue : SlotNode
{
    public required string Text { get; set; }

    public required string Tone { get; init; }

    /// <summary>
    /// True when this piece reads a variable rather than showing a value: a <c>name</c>, or a
    /// bare word in a field whose whole value is a formula. A number or a quoted string is not one,
    /// and neither is <c>true</c>, <c>false</c> or <c>null</c>.
    /// </summary>
    public bool IsName { get; init; }
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
