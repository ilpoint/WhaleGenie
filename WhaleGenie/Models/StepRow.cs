using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>What one row of the step list stands for.</summary>
public enum StepRowKind
{
    /// <summary>One step of the macro.</summary>
    Step,

    /// <summary>The title of one of a block's lists, such as the steps a loop runs.</summary>
    Head,

    /// <summary>The line that closes a block.</summary>
    Foot,
}

/// <summary>
/// One row of the step list. The editor shows a macro as a flat list of rows because that is
/// what the control, the selection and the drag commands work on, but a row also knows how deep
/// it sits and which list of steps it belongs to. That is what lets the nesting a macro is
/// written with be read straight off the list and edited in place, instead of hiding inside the
/// dialog of the block that holds it.
/// </summary>
public sealed class StepRow : ILanguageAware
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Re-reads the labels on this row; the step underneath is the same step.</summary>
    public void RefreshLanguage()
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    public required StepRowKind Kind { get; init; }

    /// <summary>The step this row is about. A head or foot row belongs to the block it opens or closes.</summary>
    public required MacroStep Step { get; init; }

    /// <summary>The list of steps this row belongs to, or null for a step at the top of the macro.</summary>
    public StepParameter? List { get; init; }

    /// <summary>Where the step sits in that list; -1 on a head or foot row, which is not a step.</summary>
    public int Index { get; init; } = -1;

    /// <summary>Where this row sits in the visible list, so a drag knows which row it is over.</summary>
    public int RowIndex { get; init; } = -1;

    /// <summary>How many blocks this row is nested in, which is what the left margin draws.</summary>
    public int Depth { get; init; }

    /// <summary>
    /// What the design-time check found wrong with this step, or empty when it found nothing.
    /// It travels with the row rather than being looked up again so the list can mark the row
    /// it came from, which is what saves the user from having to hunt for it.
    /// </summary>
    public string Problem { get; init; } = string.Empty;

    /// <summary>Left margin that indents a row by how deep it sits.</summary>
    public Thickness Indent => new(Depth * 18, 0, 0, 0);

    public bool IsStep => Kind is StepRowKind.Step;

    /// <summary>True on the title of a block's list, such as the steps a loop runs.</summary>
    public bool IsHead => Kind is StepRowKind.Head;

    /// <summary>True on the line that closes a block.</summary>
    public bool IsFoot => Kind is StepRowKind.Foot;

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

    /// <summary>
    /// True on a block that declares at least one child-step list, even while that list is empty.
    /// The first child is dragged into the opened block itself, so the arrow cannot wait for a
    /// child to exist already.
    /// </summary>
    public bool CanFold => IsStep
        && Step.Definition?.Parameters.Any(parameter =>
            parameter.Kind == ActionParameterKind.Steps) == true;

    /// <summary>Arrow on the fold button, pointing down while the block is open.</summary>
    public Lucide.Avalonia.LucideIconKind FoldIcon =>
        Step.IsExpanded ? AppIcons.ChevronDown : AppIcons.ChevronRight;

    /// <summary>What the fold button says it will do.</summary>
    public string FoldHint => Strings.Get(Step.IsExpanded ? "Editor.Fold" : "Editor.Unfold");

    /// <summary>Name of the list this head row opens, such as the steps a loop runs.</summary>
    public string HeadLabel => List is null ? string.Empty : Step.ParameterLabel(List);

    /// <summary>How many steps that list holds, so an empty block looks empty.</summary>
    public string HeadCount => List is null
        ? string.Empty
        : Strings.Format("Common.StepCount", List.Steps.Count);

    /// <summary>What the line closing this block says, named after the block it closes.</summary>
    public string FootLabel => Strings.Format("Editor.BlockEnd", Step.DisplayName);

    /// <summary>
    /// What the button that adds to this list says. A list the catalogue names — the branches of
    /// a switch — names itself, so the button says "add a branch" rather than "add a step".
    /// </summary>
    public string AddHint => List is not null
        && Step.ParameterDefinition(List) is { AddLabelKey.Length: > 0 } definition
            ? Strings.Get(definition.AddLabelKey)
            : Strings.Get("Editor.AddInside");

    /// <summary>The short words on the add button at the start of one child-step list.</summary>
    public string AddLabel => List is not null
        && Step.ParameterDefinition(List) is { AddLabelKey.Length: > 0 } definition
            ? Strings.Get(definition.AddLabelKey)
            : Strings.Get("Add.AddStepToBlock");

    /// <summary>True on the title of a list that does not hold its first step yet.</summary>
    public bool IsEmptyList => IsHead && List?.Steps.Count == 0;

    /// <summary>What an empty section says about its first action.</summary>
    public string EmptyListHint => List is not null
        && Step.ParameterDefinition(List) is { ChildKeys.Count: > 0 }
            ? Strings.Get("Editor.EmptyBranchList")
            : Strings.Get("Editor.EmptyStepList");

    // The step's own look, passed through so one row template binds one kind of thing.

    public Lucide.Avalonia.LucideIconKind? Icon => Step.Icon;

    public string DisplayName => Step.DisplayName;

    public string Detail => Step.Detail;

    public string Comment => Step.Comment;

    public bool HasComment => Step.HasComment;

    public string MetaSummary => Step.MetaSummary;

    public bool HasMetaSummary => Step.HasMetaSummary;

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
