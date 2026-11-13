using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Expressions;
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

    /// <summary>
    /// What the left of a comparison is, as the box says it. Typing reads the line once into the
    /// shape beside it, which is what the row keeps.
    /// </summary>
    public string Left
    {
        get => _left;
        set
        {
            if (SetProperty(ref _left, value))
            {
                _leftNode = Formula.FromFormula(value);
                Changed();
            }
        }
    }

    private string _left = string.Empty;

    /// <summary>
    /// The left side as the shape it is. Setting it says the line again from the shape, so a name
    /// that arrived with an identity keeps it rather than being read back as a word.
    /// </summary>
    public ExpressionNode LeftNode
    {
        get => _leftNode;
        set
        {
            _leftNode = value;
            var text = Said(value);
            if (_left != text)
            {
                _left = text;
                OnPropertyChanged(nameof(Left));
            }
        }
    }

    private ExpressionNode _leftNode = Formula.Nothing;

    /// <summary>What the right of a comparison is, when the comparison has one.</summary>
    public string Right
    {
        get => _right;
        set
        {
            if (SetProperty(ref _right, value))
            {
                _rightNode = Formula.FromFormula(value);
                Changed();
            }
        }
    }

    private string _right = string.Empty;

    /// <summary>The right side as the shape it is.</summary>
    public ExpressionNode RightNode
    {
        get => _rightNode;
        set
        {
            _rightNode = value;
            var text = Said(value);
            if (_right != text)
            {
                _right = text;
                OnPropertyChanged(nameof(Right));
            }
        }
    }

    private ExpressionNode _rightNode = Formula.Nothing;

    /// <summary>A side as the line the box shows it as.</summary>
    private static string Said(ExpressionNode value)
        => Formula.IsNothing(value) ? string.Empty : Expression.Render(value, bare: true);

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
