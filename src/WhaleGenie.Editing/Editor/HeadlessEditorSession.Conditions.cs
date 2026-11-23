using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>The condition rows inside one step parameter: groups, rows, joins and the edits on them.</summary>

internal sealed partial class HeadlessEditorSession
{
    public void AddConditionRow(string stepId, string name, int[]? path = null)
    {
        if (_actionSession is { } action)
        {
            action.AddConditionRow(name, path ?? []);
            return;
        }

        ChangeCondition(stepId, name, root =>
            GroupAt(root, path ?? []).Children.Add(ConditionDraftNode.Row()));
    }

    public void AddConditionGroup(string stepId, string name, int[]? path = null)
    {
        if (_actionSession is { } action)
        {
            action.AddConditionGroup(name, path ?? []);
            return;
        }

        ChangeCondition(stepId, name, root =>
            GroupAt(root, path ?? []).Children.Add(ConditionDraftNode.Group(Join.All)));
    }

    public void RemoveConditionNode(string stepId, string name, int[] path)
    {
        if (_actionSession is { } action)
        {
            action.RemoveConditionNode(name, path);
            return;
        }

        ChangeCondition(stepId, name, root =>
        {
            if (path.Length == 0)
            {
                throw new InvalidOperationException("the root condition cannot be removed");
            }

            var parent = GroupAt(root, path[..^1]);
            if (path[^1] < 0 || path[^1] >= parent.Children.Count)
            {
                throw new InvalidOperationException("the condition has no node " + path[^1]);
            }

            parent.Children.RemoveAt(path[^1]);
        });
    }

    public void RemoveConditionRow(string stepId, string name, int index)
        => RemoveConditionNode(stepId, name, [index]);

    public void SetConditionRow(string stepId, string name, int[] path, string field, string value)
    {
        if (_actionSession is { } action)
        {
            action.SetConditionRow(name, path, field, value);
            return;
        }

        ChangeCondition(stepId, name, root =>
        {
            if (path.Length == 0)
            {
                throw new InvalidOperationException("the root condition is a group");
            }

            var parent = GroupAt(root, path[..^1]);
            if (path[^1] < 0 || path[^1] >= parent.Children.Count
                || parent.Children[path[^1]] is not { IsRow: true } row)
            {
                throw new InvalidOperationException("the condition has no row at " + string.Join('.', path));
            }

            switch (field)
            {
                case "left":
                    row.Left = ConditionValue(value);
                    break;
                case "right":
                    row.Right = ConditionValue(value);
                    break;
                case "compare":
                    row.Compare = Enum.Parse<ConditionCompare>(value, ignoreCase: true);
                    break;
                default:
                    throw new InvalidOperationException("unknown condition field " + field);
            }
        });
    }

    public void SetConditionRow(string stepId, string name, int index, string field, string value)
        => SetConditionRow(stepId, name, [index], field, value);

    public void SetConditionJoin(string stepId, string name, string value, int[]? path = null)
    {
        if (_actionSession is { } action)
        {
            action.SetConditionJoin(name, path ?? [], value);
            return;
        }

        ChangeCondition(stepId, name, root =>
        {
            var group = GroupAt(root, path ?? []);
            group.Join = Enum.Parse<Join>(value, ignoreCase: true);
        });
    }

    private static ExpressionNode ConditionValue(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return Formula.Nothing;
        }

        if (text[0] == '$')
        {
            return Formula.Name(text[1..]);
        }

        if (bool.TryParse(text, out var flag))
        {
            return Formula.Constant(flag ? "true" : "false");
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Formula.Number(text, number)
            : Formula.Text(value);
    }

    private static ConditionDraftNode GroupAt(ConditionDraftNode root, int[] path)
    {
        var group = root;
        foreach (var index in path)
        {
            if (index < 0 || index >= group.Children.Count
                || !group.Children[index].IsGroup)
            {
                throw new InvalidOperationException("the condition has no group at " + string.Join('.', path));
            }

            group = group.Children[index];
        }

        return group;
    }

    private void ChangeCondition(string stepId, string name, Action<ConditionDraftNode> change)
    {
        var parameter = FindParameter(stepId, name);
        var draft = EnsureConditionDraft(parameter);
        change(draft.Root);
        _conditionDrafts[parameter] = draft;
        parameter.Written = Condition.ToNode(draft.Root.ToShape()) ?? Formula.Nothing;
        _model.IsDirty = true;
    }

    private ConditionDraft EnsureConditionDraft(StepParameter parameter)
    {
        if (_conditionDrafts.TryGetValue(parameter, out var draft))
        {
            return draft;
        }

        if (!Condition.TryRead(parameter.Written, out var shape))
        {
            throw new InvalidOperationException("the condition cannot be edited as rows");
        }

        draft = new ConditionDraft(ConditionDraftNode.Root(shape));
        _conditionDrafts[parameter] = draft;
        return draft;
    }

    private static ConditionDraftNode ConditionRowAt(ConditionDraftNode root, int[] path)
    {
        if (path.Length == 0)
        {
            throw new InvalidOperationException("the root condition is a group");
        }

        var parent = GroupAt(root, path[..^1]);
        if (path[^1] < 0 || path[^1] >= parent.Children.Count
            || parent.Children[path[^1]] is not { IsRow: true } row)
        {
            throw new InvalidOperationException("the condition has no row at " + string.Join('.', path));
        }

        return row;
    }

    private sealed class ConditionDraft(ConditionDraftNode root)
    {
        public ConditionDraftNode Root { get; } = root;
    }

    private sealed class ConditionDraftNode
    {
        public bool IsGroup { get; private init; }

        public bool IsRow => !IsGroup;

        public Join Join { get; set; }

        public List<ConditionDraftNode> Children { get; init; } = [];

        public ExpressionNode Left { get; set; } = Formula.Nothing;

        public ConditionCompare Compare { get; set; } = ConditionCompare.Equal;

        public ExpressionNode Right { get; set; } = Formula.Nothing;

        public static ConditionDraftNode Row() => new() { IsGroup = false };

        public static ConditionDraftNode Group(Join join) => new() { IsGroup = true, Join = join };

        public static ConditionDraftNode Root(ConditionShape shape)
        {
            if (shape is ConditionShape.Group group)
            {
                return FromShape(group);
            }

            var root = Group(Join.All);
            root.Children.Add(FromShape(shape));
            return root;
        }

        public static ConditionDraftNode FromShape(ConditionShape shape)
            => shape switch
            {
                ConditionShape.Row row => new ConditionDraftNode
                {
                    IsGroup = false,
                    Left = row.Left,
                    Compare = row.Compare,
                    Right = row.Right,
                },
                ConditionShape.Group group => new ConditionDraftNode
                {
                    IsGroup = true,
                    Join = group.Join,
                    Children = [.. group.Children.Select(FromShape)],
                },
                _ => Row(),
            };

        public ConditionShape ToShape()
            => IsGroup
                ? new ConditionShape.Group(Join, [.. Children.Select(child => child.ToShape())])
                : new ConditionShape.Row(Left, Compare, Right);
    }

}
