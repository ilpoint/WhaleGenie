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

    /// <summary>Lets only the operators that have a complete expression on their left be pressed.</summary>
    private void RefreshOperatorState()
    {
        if (!IsValueMode)
        {
            return;
        }

        var enabled = _draft.CanPutOperator
            || SelectedNode is { } picked && picked is not SlotEmpty;
        foreach (var sign in Operators)
        {
            sign.IsEnabled = enabled;
        }
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
