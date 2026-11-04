using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Media;
using WhaleGenie.Localization;

using WhaleGenie.Models;
using WhaleGenie.Views;

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
public sealed class StepRow : ILanguageAware, IFolds, INotifyPropertyChanged
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

    /// <summary>What the guide was worked out from, so it can be worked out again when a block folds.</summary>
    private int _depth;

    private IReadOnlyList<bool> _continues = [];

    private bool _hasNext;

    private bool _hasBox;

    /// <summary>
    /// True when this row has rows under it. For a block that is every list it runs, even an empty
    /// one: an empty body still has a row saying it is empty, so the line from the block's box down
    /// to that row has to be drawn. Counting steps instead leaves the column starting one row late
    /// on every block whose lists are still empty — which is every block the moment it is added.
    /// </summary>
    private bool HasChildren => IsSection
        ? List is { Steps.Count: > 0 }
        : Step.StepLists.Any();

    /// <summary>
    /// Works out the lines this row draws. <paramref name="continues"/> says, for each level above
    /// this one, whether the node at that level has a sibling after it; <paramref name="hasNext"/>
    /// is the same answer for this row itself.
    /// </summary>
    internal void Guide(int depth, IReadOnlyList<bool> continues, bool hasNext, bool hasBox)
    {
        _depth = depth;
        _continues = continues;
        _hasNext = hasNext;
        _hasBox = hasBox;

        var lines = new List<GuideLine>(depth + 2);

        // The first level hangs from a trunk of its own. Without it the top of the tree has no lines
        // at all and reads as a plain list; with it, every row arrives from one line down the left,
        // the same way the rows of a block arrive from the line above them.
        if (depth == 0)
        {
            lines.Add(new GuideLine(-ChevronCenter, hasNext, true, ChevronCenter));
        }
        else
        {
            for (var level = 0; level < depth; level++)
            {
                // A column belongs to the node one level below it: the line at level N carries the
                // connectors of that node's children, so it runs on while that node — the ancestor
                // at depth N + 1 — still has a sibling after this row's own branch. Reading it one
                // level off is a column that stops a row early.
                var mine = level == depth - 1;

                // A column runs on for either of two reasons, and both are the same sentence from
                // two sides: something below it still has to be reached. Either the child we are
                // inside has a sibling after it — the then of an if, with the else still to come —
                // or the node the column belongs to has one, which is what makes the trunk of the
                // first level run past a whole block instead of stopping at its body.
                var childAhead = mine
                    ? hasNext
                    : level + 1 < continues.Count && continues[level + 1];
                // Only the trunk has this second reason. A row's connector is drawn one column left
                // of its own box, so a sibling at depth N is reached by the column at N - 1, not at
                // N: for every column but the first, "my node has a sibling" says nothing about
                // whether this column still has a row to reach — and drawing on regardless leaves a
                // line hanging past the last row of the branch, which is the overhang this guards.
                var ownerAhead = level == 0 && continues.Count > 0 && continues[0];
                var full = childAhead || ownerAhead;
                if (!mine && !full)
                {
                    continue;
                }

                // A line hangs under the chevron of the node it belongs to, one indent step plus
                // twelve pixels left of the content of every row beneath it. Measured from this
                // row's content, level N sits at (N - depth) * step - centre.
                var left = ((level - depth) * IndentWidth) - ChevronCenter;

                // The branch arrives at the row itself, not at the slot where a fold box would be:
                // a row that has no box has nothing between the line and its icon, and a stub that
                // stops in that empty slot reads as a line that did not reach.
                var reach = !mine ? 0
                    : hasBox ? IndentWidth + ChevronCenter - ChevronSize - ChevronMargin
                    : IndentWidth + ChevronCenter;

                lines.Add(new GuideLine(left, full, mine, reach));
            }
        }

        // A row that holds steps drops a line from its own chevron down to the bottom of its row,
        // and the first child picks it up from there. Without it the column that carries a block's
        // steps starts one row too low, and the block's own chevron ends in mid-air.
        if (HasChildren && IsExpanded)
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
    public const double ChevronCenter = ChevronMargin + (ChevronSize / 2);

    public bool IsBlock => IsStep && Step.StepLists.Any();

    /// <summary>
    /// True on the row that carries the fold box. Only a step that runs steps has one: the row that
    /// names a block's body is part of that block, and a second box on it would say the body folds
    /// on its own, which it does not.
    /// </summary>
    public bool CanFold => IsBlock;

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

            // The line that drops from this row to its first child is drawn only while the children
            // are on screen. Folding does not rebuild the rows — it only stops drawing the children —
            // so that line has to be worked out again here or it stays behind, hanging under the box
            // of a block that is closed.
            if (HasChildren)
            {
                Guide(_depth, _continues, _hasNext, _hasBox);
            }

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
