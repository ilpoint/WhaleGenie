using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// The step list built from the macro, and the rows folded or revealed around the current one.
public partial class RunViewModel
{
    private void Build(IReadOnlyList<ExecutableStep> steps, int depth, RunStepViewModel? parent = null)
    {
        foreach (var step in steps)
        {
            if (step.Id.Length > 0)
            {
                _ownIds.Add(step.Id);
            }

            var row = new RunStepViewModel
            {
                Id = step.Id,
                Label = FailedStepPrompt.Name(step.Type),
                Detail = Detail(step),
                Description = ActionCatalog.Find(step.Type)?.LocalDescription ?? string.Empty,
                ActionKey = step.Type,
                Depth = depth,
                IsContainer = HoldsSteps(step),
                Comment = step.Meta.Comment.Trim(),
                CanBreak = true,
                Parent = parent,
            };

            Steps.Add(row);
            parent?.Children.Add(row);

            Build(step.Children("steps"), depth + 1, row);
            Build(step.Children("then"), depth + 1, row);
            Build(step.Children("else"), depth + 1, row);
            Build(step.Children("body"), depth + 1, row);
            Build(step.Children("catch"), depth + 1, row);
            Build(step.Children("finally"), depth + 1, row);
            Build(step.Children("conditions"), depth + 1, row);
        }
    }

    /// <summary>
    /// Shows or hides the steps inside one row. The list is flat, so folding is a decision about
    /// every row underneath: one that has a folded step above it is not shown, however deep it is.
    /// </summary>
    [RelayCommand]
    private void ToggleFold(RunStepViewModel? row)
    {
        if (row is null || !row.IsContainer)
        {
            return;
        }

        row.IsExpanded = !row.IsExpanded;
        RefreshShown();
    }

    /// <summary>Works out which rows are on screen from the folds above them.</summary>
    private void RefreshShown()
    {
        foreach (var row in Steps)
        {
            var shown = true;
            for (var above = row.Parent; above is not null; above = above.Parent)
            {
                if (!above.IsExpanded)
                {
                    shown = false;
                    break;
                }
            }

            row.IsShown = shown;
        }
    }

    /// <summary>Opens the folds above a step, so the row the run is standing in can be seen.</summary>
    private void RevealAncestors(RunStepViewModel row)
    {
        for (var above = row.Parent; above is not null; above = above.Parent)
        {
            above.IsExpanded = true;
        }

        RefreshShown();
    }

    /// <summary>Whether a step holds steps of its own, in any of the lists a block can have.</summary>
    private static bool HoldsSteps(ExecutableStep step)
        => step.Children("steps").Count > 0
            || step.Children("then").Count > 0
            || step.Children("else").Count > 0
            || step.Children("body").Count > 0
            || step.Children("catch").Count > 0
            || step.Children("finally").Count > 0
            || step.Children("conditions").Count > 0;

    internal static string Detail(ExecutableStep step)
    {
        // The fields are named the way the action's form names them: a row saying "url = ..." next
        // to a form saying "网址" makes the reader match two vocabularies for one field.
        var labels = ActionCatalog.Find(step.Type)?.Parameters
            .ToDictionary(parameter => parameter.Name, parameter => parameter.LocalLabel,
                StringComparer.OrdinalIgnoreCase);

        var parts = new List<string>();
        foreach (var parameter in step.Parameters)
        {
            // A field nobody answered says nothing here: the row is a summary of what the step was
            // given, so an empty field would only add a name with nothing after it.
            var line = FormulaText.Write(parameter.Written).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var label = labels?.GetValueOrDefault(parameter.Name) ?? parameter.Name;
            parts.Add($"{label} = {line}");
        }

        // The step's own name goes first: it is what a breakpoint, a failure key and the log call
        // this step by, so the row has to say it too.
        // The row is written in the reader's words throughout: the action's own key is in the
        // bubble, where somebody looking for it can find it without reading it on every row.
        var written = string.Join(", ", parts);
        var text = step.Id.Length == 0 ? written : $"{step.Id} · {written}";
        if (text.Length == 0)
        {
            return step.Type;
        }

        return text.Length <= 90 ? text : text[..87] + "\u2026";
    }
}
