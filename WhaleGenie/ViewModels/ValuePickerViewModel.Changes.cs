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

// What a change of text does, and the function list a group filters down to.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
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

    partial void OnSelectedFunctionGroupChanged(ExpressionFunctionFilter? value)
    {
        // One button always stands for the list below it, so the row says which group is on show.
        foreach (var filter in FunctionGroups)
        {
            filter.IsSelected = ReferenceEquals(filter, value);
        }

        RefreshFunctions();
    }

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
}
