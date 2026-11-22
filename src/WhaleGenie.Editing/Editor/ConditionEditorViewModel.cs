using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>One comparison offered in a row's dropdown, and the operator it writes.</summary>
/// <remarks>
/// The words themselves live in <see cref="Comparisons"/>, so a condition row and a switch branch
/// are named the same thing. This type only answers the questions a row asks of the one it picked.
/// </remarks>

public sealed partial class ConditionEditorViewModel : ViewModelBase
{
    private readonly Action _changed;
    private bool _writing;

    public ConditionEditorViewModel(ExpressionNode? written, Action changed,
        IReadOnlyList<string>? suggestions = null,
        IReadOnlyList<VariableChoice>? variables = null)
    {
        _changed = changed;
        Suggestions = suggestions ?? [];
        Variables = variables ?? [];
        Root = ConditionNodeViewModel.Group(Join.All, Changed);
        Read(written ?? Formula.Nothing);

        // A condition with nothing in it opens as one empty comparison rather than as no rows at
        // all: three boxes with a word in each say what a condition is made of, while an empty
        // space under a heading leaves a person to guess. The row is empty, so what the step holds
        // is still the empty text it started with.
        if (Rows.Count == 0)
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
    public bool HasInput => Root.Children.Any(AnyInput);

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

    /// <summary>The condition written out as the expression the engine reads.</summary>
    public string Text => Condition.Render(ShapeOf(Root));

    /// <summary>
    /// The condition as the shape a step keeps. This is what the field takes: the rows are made of
    /// shapes, so handing the step a line would read every name in it back as a word.
    /// </summary>
    public ExpressionNode Written => Condition.ToNode(ShapeOf(Root)) ?? Formula.Nothing;

    /// <summary>The model read back as the neutral shape the writer takes.</summary>
    private static ConditionShape ShapeOf(ConditionNodeViewModel node)
        => node.IsGroup
            ? new ConditionShape.Group(node.Join, [.. node.Children.Select(ShapeOf)])
            : new ConditionShape.Row(node.LeftNode, node.Compare, node.RightNode);

    /// <summary>True when the condition has nothing in it yet.</summary>
    public bool IsEmpty => Rows.Count == 0;

    /// <summary>Reads a condition back into the model. Every shape is one the rows can say.</summary>
    public void Read(ExpressionNode written)
    {
        _writing = true;
        try
        {
            Root.Children.Clear();
            if (Formula.IsNothing(written))
            {
                Root.Join = Join.All;
                return;
            }

            Condition.TryRead(written, out var shape);
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
        row.LeftNode = shape.Left;
        row.Compare = shape.Compare;
        row.RightNode = shape.Right;
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

        // The wall offers names, not lines. A caret in the middle of a cell is a completion of the
        // name it holds, so the cell ends up holding one name shape — there is no line to parse
        // and no sign in front of the name to strip.
        if (right)
        {
            node.RightNode = Formula.Name(written);
        }
        else
        {
            node.LeftNode = Formula.Name(written);
        }
    }

    /// <summary>Puts an expression the builder worked out into one side of a row.</summary>
    public void WriteExpression(ConditionNodeViewModel node, bool right, ExpressionNode expression)
    {
        if (right)
        {
            node.RightNode = expression;
        }
        else
        {
            node.LeftNode = expression;
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
