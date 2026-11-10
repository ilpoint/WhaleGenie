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

// How the recipe is stacked to fit, and the labels it shows in the reader language.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>
    /// Says which calls need one row per argument. The band gives it the width it actually has, so
    /// narrowing the window can fold a call that fitted on a wide one.
    /// </summary>
    public void RefreshStacking(double availableWidth)
    {
        if (Recipe is null)
        {
            return;
        }

        // Before the first layout pass the viewport has no width; the compact estimate is the stand
        // in until the band can say how much room it really has.
        var width = availableWidth > 0 ? availableWidth : SlotStacking.CompactWidthPx;
        Fold(Recipe, width, insideStacked: false);
    }

    /// <summary>
    /// One level folds at a time. A call already drawn one row per argument gives its nested calls
    /// a whole row each to lie on; folding those too would multiply the height and push the rest of
    /// the formula out of the scrollable strip.
    /// </summary>
    private static void Fold(SlotNode node, double width, bool insideStacked)
    {
        if (node is SlotCall call)
        {
            var stacked = !insideStacked && SlotStacking.Decide(call, width);
            call.Stacked = stacked;
            foreach (var argument in call.Arguments)
            {
                Fold(argument, width, insideStacked || stacked);
            }

            return;
        }

        if (node is SlotSequence sequence)
        {
            foreach (var piece in sequence.Items)
            {
                Fold(piece, width, insideStacked);
            }
        }
    }

    /// <summary>A label written from the text table, read again when the language changes.</summary>
    public override void RefreshLanguage()
    {
        base.RefreshLanguage();
        RebuildRecipe();
    }

    /// <summary>True when what is typed names this function, its call, or what it does.</summary>
    private static bool Matches(ExpressionFunctionChoice function, string query)
        => function.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Signature.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Heading.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.ChineseSignature.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Hint.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>All of the language, then each group once, in the order the language states them.</summary>
    private IReadOnlyList<ExpressionFunctionFilter> BuildGroups()
    {
        var groups = new List<ExpressionFunctionFilter> { new(string.Empty, "ExprBuilder.FunctionsAll") };
        foreach (var function in _functions)
        {
            if (groups.All(filter => filter.Group != function.Group))
            {
                groups.Add(new ExpressionFunctionFilter(function.Group, $"Expr.Group.{function.Group}"));
            }
        }

        return groups;
    }
}
