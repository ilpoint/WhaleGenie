using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Views;

namespace WhaleGenie.ViewModels;

/// <summary>One comparison offered in a row's dropdown, and the operator it writes.</summary>
/// <remarks>
/// The words themselves live in <see cref="Comparisons"/>, so a condition row and a switch branch
/// are named the same thing. This type only answers the questions a row asks of the one it picked.
/// </remarks>

/// <summary>
/// One node of a condition: either one comparison, or a group of them joined by "and" or "or".
/// The same object is the line the table shows and the node the tree shows, which is what lets
/// the two views edit one condition rather than two copies of it.
/// </summary>
public sealed partial class ConditionNodeViewModel : ObservableObject, IFolds
{
    private readonly Action _changed;

    private ConditionNodeViewModel(bool group, Action changed)
    {
        IsGroup = group;
        _changed = changed;
    }

    /// <summary>A group of conditions joined by one way of joining.</summary>
    public static ConditionNodeViewModel Group(Join join, Action changed)
        => new(true, changed) { Join = join };

    /// <summary>One comparison.</summary>
    public static ConditionNodeViewModel Row(Action changed)
        => new(false, changed) { Compare = ConditionCompare.Equal };

    public bool IsGroup { get; }

    /// <summary>True on a node that can be folded open: a group, not a single comparison.</summary>
    public bool CanFold => IsGroup;

    public bool IsRow => !IsGroup;

    /// <summary>What hangs under a group. Empty on a comparison.</summary>
    public ObservableCollection<ConditionNodeViewModel> Children { get; } = [];

    /// <summary>
    /// How the group holds: every line of it, any line of it, or none of them. A group of one line
    /// reads the same all three ways, and the words on its line say which one was picked.
    /// </summary>
    public Join Join
    {
        get => _join;
        set
        {
            if (SetProperty(ref _join, value))
            {
                OnPropertyChanged(nameof(OperatorIndex));
                OnPropertyChanged(nameof(OperatorLabel));
                Changed();
            }
        }
    }

    private Join _join;

    /// <summary>0, 1 or 2 in the order the dropdown offers them, which is what it binds to.</summary>
    public int OperatorIndex
    {
        get => (int)Join;
        set
        {
            if (value is >= 0 and <= 2)
            {
                Join = (Join)value;
            }
        }
    }

    /// <summary>The words on a group's own line: all of these, any of these, none of these.</summary>
    public string OperatorLabel => Strings.Get(Join switch
    {
        Join.Any => "Condition.Any",
        Join.None => "Condition.None",
        _ => "Condition.All",
    });

    /// <summary>What the left of a comparison is: a variable, an expression or a literal.</summary>
    public string Left
    {
        get => _left;
        set
        {
            if (SetProperty(ref _left, value))
            {
                Changed();
            }
        }
    }

    private string _left = string.Empty;

    /// <summary>What the right of a comparison is, when the comparison has one.</summary>
    public string Right
    {
        get => _right;
        set
        {
            if (SetProperty(ref _right, value))
            {
                Changed();
            }
        }
    }

    private string _right = string.Empty;

    /// <summary>How this line compares the two sides.</summary>
    public ConditionCompare Compare
    {
        get => _compare;
        set
        {
            if (SetProperty(ref _compare, value))
            {
                OnPropertyChanged(nameof(Choice));
                OnPropertyChanged(nameof(NeedsValue));
                Changed();
            }
        }
    }

    private ConditionCompare _compare;

    /// <summary>True when this comparison needs a right-hand side, so the box for it is drawn.</summary>
    public bool NeedsValue => Compare is not (ConditionCompare.Holds or ConditionCompare.Fails);

    /// <summary>
    /// Which side of the row was last typed in. The two buttons at the end of a row fill one cell
    /// and the row has two, so the one they fill is the one the caret was just in; a row nobody has
    /// typed in yet fills its left, which is the side a comparison starts from.
    /// </summary>
    public bool ActiveRight { get; set; }

    /// <summary>The side the row buttons fill, which is the left one when there is no right to fill.</summary>
    public bool FillsRight => ActiveRight && NeedsValue;

    /// <summary>The comparison as the dropdown shows it.</summary>
    public ConditionCompareChoice Choice
    {
        get => Condition.CompareChoices.First(choice => choice.Compare == Compare);
        set
        {
            if (value is not null)
            {
                Compare = value.Compare;
            }
        }
    }

    /// <summary>Every comparison a line may make.</summary>
    public IReadOnlyList<ConditionCompareChoice> Choices => Condition.CompareChoices;

    /// <summary>Removes this node from the group holding it.</summary>
    public ConditionNodeViewModel? Parent
    {
        get => _parent;
        internal set
        {
            if (SetProperty(ref _parent, value))
            {
                OnPropertyChanged(nameof(CanRemove));
            }
        }
    }

    private ConditionNodeViewModel? _parent;

    /// <summary>
    /// True on a group that can be taken out of the condition. The outermost group is the condition
    /// itself, so it has no cross: what would be removed is the whole thing, not a line of it.
    /// </summary>
    public bool CanRemove => Parent is { IsGroup: true };

    private void Changed()
    {
        OnPropertyChanged(nameof(OperatorLabel));
        _changed();
    }
}

/// <summary>
/// The condition of one step, edited two ways at once: as a table of comparisons, which is how a
/// person fills in "a and b and c" quickly, and as a tree, which is the only way to say "a and
/// (b or c)". Both views sit on this one model, so switching views is switching a template rather
/// than converting anything, and neither can disagree with the other.
/// </summary>
public sealed partial class ConditionEditorViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _writing;

    public ConditionEditorViewModel(string? text, Action changed, IReadOnlyList<string>? suggestions = null,
        IReadOnlyList<VariableChoice>? variables = null)
    {
        _changed = changed;
        Suggestions = suggestions ?? [];
        Variables = variables ?? [];
        Root = ConditionNodeViewModel.Group(Join.All, Changed);
        Read(text ?? string.Empty);

        // A condition with nothing in it opens as one empty comparison rather than as no rows at
        // all: three boxes with a word in each say what a condition is made of, while an empty
        // space under a heading leaves a person to guess. The row is empty, so what the step holds
        // is still the empty text it started with.
        if (!IsRaw && Rows.Count == 0)
        {
            _writing = true;
            try
            {
                AddRow();
            }
            finally
            {
                _writing = false;
            }
        }
    }

    /// <summary>Names and functions offered while a side of a comparison is typed in.</summary>
    public IReadOnlyList<string> Suggestions { get; }

    /// <summary>
    /// The variables the cell beside a comparison may name. A condition is written as an expression,
    /// so the picker a value field opens is the one that belongs here too: a name should never have
    /// to be typed out of memory in a place that is already about reading names.
    /// </summary>
    public IReadOnlyList<VariableChoice> Variables { get; }

    /// <summary>What the left of an empty comparison says it wants, by example.</summary>
    public string LeftPlaceholder => Strings.Get("Condition.LeftPlaceholder");

    /// <summary>What the right of an empty comparison says it wants, by example.</summary>
    public string RightPlaceholder => Strings.Get("Condition.RightPlaceholder");

    /// <summary>
    /// True once anything has been written into the condition. A brand new block has a row waiting
    /// and nothing in it, which is a step somebody has not got to yet rather than a mistake; a row
    /// half filled in is.
    /// </summary>
    public bool HasInput => IsRaw
        ? RawText.Trim().Length > 0
        : Root.Children.Any(AnyInput);

    private static bool AnyInput(ConditionNodeViewModel node)
        => node.IsGroup
            ? node.Children.Any(AnyInput)
            : node.Left.Trim().Length > 0 || node.Right.Trim().Length > 0;

    /// <summary>The whole condition as one tree, so the group's own line is the tree's first row.</summary>
    public IReadOnlyList<ConditionNodeViewModel> TreeRoots => [Root];

    /// <summary>The whole condition's way of joining, which is what the dropdown binds to.</summary>
    public int OperatorIndex
    {
        get => Root.OperatorIndex;
        set
        {
            if (Root.OperatorIndex != value)
            {
                Root.OperatorIndex = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>The whole condition. A condition written as one line is a group holding one row.</summary>
    public ConditionNodeViewModel Root { get; }

    /// <summary>What the table shows: the top level of the condition, one line each.</summary>
    public ObservableCollection<ConditionNodeViewModel> Rows => Root.Children;

    /// <summary>True while the tree is the view on screen.</summary>
    public bool ShowTree
    {
        get => _tree;
        set
        {
            if (SetProperty(ref _tree, value))
            {
                OnPropertyChanged(nameof(ShowTable));
            }
        }
    }

    private bool _tree;

    public bool ShowTable => !ShowTree;

    /// <summary>
    /// True when the condition nests, so the table cannot say all of it. The table is then shown
    /// read-only in spirit: its button offers the tree, and the rows are still there to look at.
    /// </summary>
    public bool Nests => Rows.Any(row => row.IsGroup);

    /// <summary>
    /// True when the text the step held could not be read as conditions — a function call, an
    /// odd shape — so it is kept as it was and edited as text rather than guessed at.
    /// </summary>
    public bool IsRaw
    {
        get => _raw;
        private set
        {
            if (SetProperty(ref _raw, value))
            {
                OnPropertyChanged(nameof(ShowTable));
                OnPropertyChanged(nameof(ShowTree));
            }
        }
    }

    private bool _raw;

    /// <summary>The condition as it was written, for the case the conditions cannot be read back.</summary>
    public string RawText
    {
        get => _rawText;
        set
        {
            if (SetProperty(ref _rawText, value))
            {
                _changed();
            }
        }
    }

    private string _rawText = string.Empty;

    /// <summary>The condition written out as the expression the engine reads.</summary>
    public string Text => IsRaw ? RawText : Condition.Render(ShapeOf(Root));

    /// <summary>The model read back as the neutral shape the writer takes.</summary>
    private static ConditionShape ShapeOf(ConditionNodeViewModel node)
        => node.IsGroup
            ? new ConditionShape.Group(node.Join, [.. node.Children.Select(ShapeOf)])
            : new ConditionShape.Row(node.Left, node.Compare, node.Right);

    /// <summary>True when the condition has nothing in it yet.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Reads a condition back into the model, or keeps it as text when it does not read.</summary>
    public void Read(string text)
    {
        _writing = true;
        try
        {
            RawText = text;
            Root.Children.Clear();
            if (text.Trim().Length == 0)
            {
                IsRaw = false;
                Root.Join = Join.All;
                return;
            }

            if (!Condition.TryRead(text, out var shape))
            {
                IsRaw = true;
                return;
            }

            IsRaw = false;
            Root.Join = shape is ConditionShape.Group group ? group.Join : Join.All;
            foreach (var child in Condition.Children(shape))
            {
                Adopt(Root, From(child));
            }
        }
        finally
        {
            _writing = false;
        }

        RaiseAll();
    }

    /// <summary>One node of the model built out of one piece of the condition as it was read.</summary>
    private ConditionNodeViewModel From(ConditionShape shape)
        => shape is ConditionShape.Group group
            ? GroupNode(group)
            : RowNode((ConditionShape.Row)shape);

    private ConditionNodeViewModel GroupNode(ConditionShape.Group shape)
    {
        var group = ConditionNodeViewModel.Group(shape.Join, Changed);
        foreach (var child in shape.Children)
        {
            Adopt(group, From(child));
        }

        return group;
    }

    private ConditionNodeViewModel RowNode(ConditionShape.Row shape)
    {
        var row = ConditionNodeViewModel.Row(Changed);
        row.Left = shape.Left;
        row.Compare = shape.Compare;
        row.Right = shape.Right;
        return row;
    }

    /// <summary>Adds an empty comparison to a group — the bottom one unless another is named.</summary>
    [RelayCommand]
    public void AddRow(ConditionNodeViewModel? group = null)
    {
        var target = group is { IsGroup: true } ? group : Root;
        Adopt(target, ConditionNodeViewModel.Row(Changed));
        Changed();
    }

    /// <summary>Adds an empty group to a group, which is what makes a condition nest.</summary>
    [RelayCommand]
    public void AddGroup(ConditionNodeViewModel? group = null)
    {
        var target = group is { IsGroup: true } ? group : Root;
        Adopt(target, ConditionNodeViewModel.Group(Join.Any, Changed));
        Changed();
    }

    /// <summary>Takes one line out of the condition.</summary>
    [RelayCommand]
    public void Remove(ConditionNodeViewModel? node)
    {
        if (node is null)
        {
            return;
        }

        (node.Parent ?? Root).Children.Remove(node);
        node.Parent = null;
        Changed();
    }

    /// <summary>Shows the condition as a tree, which is the only view that can hold a nesting.</summary>
    [RelayCommand]
    private void ShowTreeView() => ShowTree = true;

    /// <summary>Shows the condition as a table of comparisons.</summary>
    [RelayCommand]
    private void ShowTableView() => ShowTree = false;

    /// <summary>Switches the view. The condition itself does not move.</summary>
    public void Show(bool tree) => ShowTree = tree;

    /// <summary>
    /// Puts a name into one side of a row, at the caret when the box in front of it knows where that
    /// is. The cell holds one value, so a name is inserted where the person was typing rather than
    /// replacing what is already there: half a name followed by a variable is a common way to write one.
    /// </summary>
    public void InsertVariable(ConditionNodeViewModel node, bool right, string token, int? caret)
    {
        if (token.Length == 0)
        {
            return;
        }

        var text = right ? node.Right : node.Left;
        var at = Math.Clamp(caret ?? text.Length, 0, text.Length);
        var written = text[..at] + token + text[at..];

        if (right)
        {
            node.Right = written;
        }
        else
        {
            node.Left = written;
        }
    }

    /// <summary>Puts an expression the builder worked out into one side of a row.</summary>
    public void WriteExpression(ConditionNodeViewModel node, bool right, string expression)
    {
        if (right)
        {
            node.Right = expression;
        }
        else
        {
            node.Left = expression;
        }
    }

    private void Adopt(ConditionNodeViewModel parent, ConditionNodeViewModel child)
    {
        child.Parent = parent;
        parent.Children.Add(child);
    }

    private void Changed()
    {
        if (_writing)
        {
            return;
        }

        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Nests));
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(OperatorIndex));
        _changed();
    }

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(Rows));
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Nests));
        OnPropertyChanged(nameof(HasInput));
        OnPropertyChanged(nameof(ShowTable));
        _changed();
    }
}
