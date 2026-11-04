using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
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

    /// <summary>
    /// The indent guides this row draws to its left: one cell per level it is nested in. Cells are
    /// laid side by side, each 16 wide with a 1px line on its left, so stacked rows line up into a
    /// continuous guide down a block's body and stop where the block does.
    /// </summary>
    public IReadOnlyList<GuideCell> Guides => _guides;

    private IReadOnlyList<GuideCell> _guides = [];

    /// <summary>Width of the guide strip, which is also how far left of the row it starts.</summary>
    public double GuideWidth => Guides.Count * 16;

    /// <summary>Moves the strip left so its cells land on the indents of the rows it belongs to.</summary>
    public Thickness GuideMargin => new(-GuideWidth, 0, 0, 0);

    /// <summary>True when this row sits inside a block, which is what the faint band marks.</summary>
    public bool IsNested => Depth > 0;

    /// <summary>True when this row hangs under something, so a stub joins it to the line above.</summary>
    public bool HasElbow => Guides.Count > 0;

    /// <summary>The stub that joins this row to its parent's line, one indent wide.</summary>
    public Thickness ElbowMargin => new(-16, 0, 0, 0);

    /// <summary>Sets the guides this row draws; called when the row is laid into the tree.</summary>
    internal void Guide(int depth)
    {
        _guides = [.. Enumerable.Range(0, depth).Select(index => new GuideCell(index))];
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Guides)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GuideWidth)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GuideMargin)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasElbow)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ElbowMargin)));
    }

    /// <summary>True on the title of one of a block's lists.</summary>
    public bool IsSection => Kind is StepRowKind.Section;

    /// <summary>True on a step the check has something to say about.</summary>
    public bool HasProblem => Problem.Length > 0;

    /// <summary>
    /// The short name this step goes by, shown on its row: it is what a result variable and a
    /// condition call the step, so it has to be readable off the list rather than hunted for.
    /// </summary>
    public string Id => Step.Id;

    /// <summary>True when the step has a name to show yet — steps gain one when they are added.</summary>
    public bool HasId => IsStep && Step.Id.Length > 0;

    /// <summary>What hovering the name says: what it is for, and how to use it.</summary>
    public string IdHint => HasId && IsStep ? Strings.Format("Editor.StepIdHint", Step.Id) : string.Empty;

    /// <summary>True on a step that runs steps of its own.</summary>
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

/// <summary>One level of the indent guide: a 16-wide cell drawing a 1px line on its left edge.</summary>
public sealed record GuideCell(int Level)
{
    /// <summary>How wide one level is, which is also the indent step the tree uses.</summary>
    public double Width => 16;
}
