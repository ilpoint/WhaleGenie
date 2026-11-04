using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using WhaleGenie.Localization;

using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>What one row of the step list stands for.</summary>
public enum StepRowKind
{
    /// <summary>One step of the macro.</summary>
    Step,

    /// <summary>
    /// One child list of a block that runs more than one of them — the "then" and the "else" of an
    /// if, the body, the catch and the finally of a try. A block that runs a single list needs no
    /// such row: its steps hang straight under it.
    /// </summary>
    Section,
}

/// <summary>
/// One node of the step tree. The editor shows a macro as a tree because that is what it is: a
/// step that runs other steps holds them, and the nesting is the structure rather than an
/// indentation worked out from a flat list.
///
/// A node is also the row it is drawn as: a <see cref="StepRowKind.Step"/> is the step itself and a
/// <see cref="StepRowKind.Section"/> is the title of one list a block runs. A block has no closing
/// row: what ends it is the indent, drawn as a guide line down its children and gone on the row
/// after them.
/// </summary>
public sealed class StepRow : ILanguageAware, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Told when a row opens or closes, so the editor can let go of steps it hides.</summary>
    public Action<StepRow, bool>? Folded { get; init; }

    /// <summary>Re-reads the labels on this row; the step underneath is the same step.</summary>
    public void RefreshLanguage()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public required StepRowKind Kind { get; init; }

    /// <summary>The step this row is about. A section or foot row belongs to the block it is part of.</summary>
    public required MacroStep Step { get; init; }

    /// <summary>The list of steps this row belongs to, or null for a step at the top of the macro.</summary>
    public StepParameter? List { get; init; }

    /// <summary>Where the step sits in that list; -1 on a section or foot row, which is not a step.</summary>
    public int Index { get; init; } = -1;

    /// <summary>Where this row sits in the document, counted over the whole tree.</summary>
    public int RowIndex { get; internal set; } = -1;

    /// <summary>How deep this row sits, which is what a drop marker lines itself up with.</summary>
    public int Depth { get; init; }

    /// <summary>What hangs under this row: a block's steps, or a section's steps.</summary>
    public List<StepRow> Children { get; } = [];

    /// <summary>The same children, for a tree that reads them as an item source.</summary>
    public IReadOnlyList<StepRow> Items => Children;

    /// <summary>
    /// What the design-time check found wrong with this step, or empty when it found nothing.
    /// It travels with the row rather than being looked up again so the list can mark the row
    /// it came from, which is what saves the user from having to hunt for it.
    /// </summary>
    public string Problem { get; init; } = string.Empty;

    public bool IsStep => Kind is StepRowKind.Step;

    /// <summary>True on the title of one of a block's lists.</summary>
    public bool IsSection => Kind is StepRowKind.Section;

    /// <summary>True on a step the check has something to say about.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>
    /// The guide lines this row draws to its left: one per level it is nested in. A line runs the
    /// whole height of the row when the node it belongs to has a sibling after it, and only down to
    /// the row's middle when that node is the last of its list — which is how a tree says "this
    /// branch ends here" with a corner instead of a line that keeps going.
    /// </summary>
    public IReadOnlyList<GuideLine> Guides => _guides;

    private IReadOnlyList<GuideLine> _guides = [];

    /// <summary>
    /// Works out the lines this row draws. <paramref name="continues"/> says, for each level above
    /// this one, whether the node at that level has a sibling after it; <paramref name="hasNext"/>
    /// is the same answer for this row itself.
    /// </summary>
    internal void Guide(int depth, IReadOnlyList<bool> continues, bool hasNext, bool hasChildren)
    {
        var lines = new List<GuideLine>(depth);
        for (var level = 0; level < depth; level++)
        {
            var mine = level == depth - 1;
            var full = mine ? hasNext : level < continues.Count && continues[level];
            if (!mine && !full)
            {
                continue;
            }

            // A line hangs under the chevron of the node it belongs to, and that chevron is one
            // indent step plus twelve pixels left of the content of every row beneath it. Measured
            // from this row's content, level N sits at (N - depth) * step - centre. The elbow runs
            // from there rightwards to the left edge of this row's own chevron box, which is where
            // a child's branch is expected to arrive.
            var left = ((level - depth) * IndentWidth) - ChevronCenter;
            lines.Add(new GuideLine(left, full, mine,
                mine ? IndentWidth + ChevronCenter - ChevronSize - ChevronMargin : 0));
        }

        // A row that holds steps drops a line from its own chevron down to the bottom of its row,
        // and the first child picks it up from there. Without it the column that carries a block's
        // steps starts one row too low, and the block's own chevron ends in mid-air.
        if (hasChildren)
        {
            lines.Add(new GuideLine(-ChevronCenter, true, false, 0, Tail: true));
        }

        _guides = lines;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Guides)));
    }

    /// <summary>
    /// One step of the tree's indentation, in pixels. The tree itself indents by this much — it is
    /// the `TreeViewItemIndent` resource in App.axaml — and the guides are placed from the same
    /// number, so a line can never sit a pixel or two off the chevron it belongs under.
    /// </summary>
    public const double IndentWidth = 26;

    /// <summary>Gap between a node's left edge and its chevron box.</summary>
    private const double ChevronMargin = 12;

    /// <summary>Side of the chevron box.</summary>
    private const double ChevronSize = 14;

    /// <summary>Where the centre of a level's chevron sits, measured from that level's left edge.</summary>
    private const double ChevronCenter = ChevronMargin + (ChevronSize / 2);

    public bool IsBlock => IsStep && Step.StepLists.Any();

    /// <summary>
    /// How a block reads against the steps it holds: heavier and a size up, so the two levels are
    /// told apart by weight before the indent is even looked at.
    /// </summary>
    public string NameWeight => IsBlock ? "SemiBold" : "Normal";

    public double NameSize => IsBlock ? 13 : 12.5;

    public double IconSize => IsBlock ? 16 : 14;

    /// <summary>True while what hangs under this row is on screen.</summary>
    public bool IsExpanded
    {
        get => IsSection ? List?.IsExpanded ?? true : Step.IsExpanded;
        set
        {
            if (IsSection)
            {
                if (List is { } list)
                {
                    list.IsExpanded = value;
                }
            }
            else
            {
                Step.IsExpanded = value;
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            Folded?.Invoke(this, value);
        }
    }

    /// <summary>True on the row the editor's selection sits on, which is what lights the card up.</summary>
    public bool IsPicked
    {
        get => _picked;
        set
        {
            if (_picked == value)
            {
                return;
            }

            _picked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPicked)));
        }
    }

    private bool _picked;

    /// <summary>Name of the list a section opens, such as the steps a loop runs.</summary>
    public string HeadLabel => List is null ? string.Empty : Step.ParameterLabel(List);

    /// <summary>How many steps that list holds, so an empty branch looks empty.</summary>
    public string HeadCount => List is null
        ? string.Empty
        : Strings.Format("Common.StepCount", List.Steps.Count);

    /// <summary>
    /// The list a new step added from this row goes into: a section's own list, or the one list a
    /// block runs when it runs only that.
    /// </summary>
    public StepParameter? Body => IsSection
        ? List
        : IsBlock && Step.StepLists.Count() == 1
            ? Step.StepLists.First()
            : null;

    /// <summary>True on the title of a list that does not hold its first step yet.</summary>
    public bool IsEmptyList => IsSection && List?.Steps.Count == 0;

    /// <summary>What an empty section says about its first action.</summary>
    public string EmptyListHint => List is not null
        && Step.ParameterDefinition(List) is { ChildKeys.Count: > 0 }
            ? Strings.Get("Editor.EmptyBranchList")
            : Strings.Get("Editor.EmptyStepList");

    // The step's own look, passed through so one row template binds one kind of thing.

    /// <summary>
    /// The icon of the action this row stands for. It is worked out here rather than kept on the
    /// step: which artwork an action gets is the interface's business, and a step read from a
    /// package has no business carrying a drawing with it.
    /// </summary>
    public Lucide.Avalonia.LucideIconKind? Icon => Step.Definition is { } definition
        ? ActionCategoryPresentation.IconFor(definition.Category)
        : null;

    public string DisplayName => Step.DisplayName;

    /// <summary>True when this row sits inside a block, which is what the faint band marks.</summary>
    public bool IsNested => Depth > 0;

    /// <summary>
    /// The short name this step goes by, shown on its row: it is what a result variable and a
    /// condition call the step, so it has to be readable off the list rather than hunted for.
    /// </summary>
    public string Id => Step.Id;

    /// <summary>True when the step has a name to show yet — steps gain one when they are added.</summary>
    public bool HasId => IsStep && Step.Id.Length > 0;

    /// <summary>What hovering the name says: what it is for, and how to use it.</summary>
    public string IdHint => HasId && IsStep ? Strings.Format("Editor.StepIdHint", Step.Id) : string.Empty;

    public string Detail => Step.Detail;

    /// <summary>True when this step has a line to say about itself at all.</summary>
    public bool HasDetail => Detail.Length > 0;

    public string Comment => Step.Comment;

    public bool HasComment => Step.HasComment;

    public string MetaSummary => Step.MetaSummary;

    public bool HasMetaSummary => Step.HasMetaSummary;

    /// <summary>
    /// True on the row the click is on. That row opens **in place** to show what does not fit on
    /// the line — the rest of the summary, the comment, the beat it runs at — so reading one step
    /// does not move the list or push the others apart; every other row stays one line.
    /// </summary>
    public bool ShowsDetail
    {
        get => _showsDetail;
        set
        {
            if (_showsDetail == value)
            {
                return;
            }

            _showsDetail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShowsDetail)));
        }
    }

    private bool _showsDetail;

    public bool IsSkipped => Step.IsSkipped;

    public string SkipLabel => Step.SkipLabel;

    /// <summary>Where the step's input goes, written on the row so it need not be opened to be read.</summary>
    public string InputMark => Step.InputMark;

    public bool HasInputMark => IsStep && InputMark.Length > 0;

    public string InputMarkHint => HasInputMark
        ? Strings.Format("Editor.InputMarkHint", InputMark)
        : string.Empty;

    public double RowOpacity => Step.RowOpacity;
}

/// <summary>
/// One line of the indent guide: where it sits, how far down it runs, and whether it turns right
/// at its middle to join the row it leads to.
/// </summary>
/// <param name="Left">X of the vertical line, measured from the row's content — usually negative.</param>
/// <param name="Full">True when the node's line carries on below this row, false when it stops here.</param>
/// <param name="Elbow">True on the row's own connector, the one that turns into it.</param>
/// <param name="ElbowWidth">How far the connector reaches to the right.</param>
/// <param name="Tail">True on the line that drops from a row's own chevron to the row's bottom.</param>
public sealed record GuideLine(double Left, bool Full, bool Elbow, double ElbowWidth, bool Tail = false);
