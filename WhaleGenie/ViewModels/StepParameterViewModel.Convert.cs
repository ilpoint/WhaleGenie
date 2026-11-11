using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// Turning the field into a saved parameter and back.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>Flattens the edited value into the model the step is built from.</summary>
    public StepParameter ToStepParameter() => new()
    {
        Name = Definition.Name,
        Kind = Definition.Kind,
        Written = WrittenValue(),
        // A blank field carries nothing to move, so a give typed beside nothing is dropped with it.
        Jitter = IsIncluded && JitterPercent is > 0m
            ? Math.Min(100m, JitterPercent.Value) / 100m
            : 0m,
        Steps = Definition.Kind is ActionParameterKind.Steps
            ? List?.Steps.ToList() ?? []
            : [],
        Rows = Definition.Kind switch
        {
            ActionParameterKind.Region => [.. Regions.Select(row => row.ToRow())],
            ActionParameterKind.ColourPoints => [.. ColourPointRows.Select(row => row.ToRow())],
            ActionParameterKind.KeySequence => [.. KeyRows.Select(row => row.ToRow())],
            ActionParameterKind.Images => [.. Pictures.Select(picture => picture.ToRow())],
            ActionParameterKind.Narrow => [.. NarrowRows.Select(row => row.ToRow())
                .Where(row => row is not null)
                .Select(row => row!)],
            ActionParameterKind.Returns => [.. ReturnsRows.Select(row => row.ToRow())
                .Where(row => row is not null)
                .Select(row => row!),
                .. Kept()],
            _ => [],
        },
    };

    /// <summary>
    /// The value as the shape the step keeps. The field is read the way its own definition says it
    /// is read — a name is a name, a whole value is an expression or a piece of text, a field of
    /// text has the names in it filled in where they stand — and it is read once, here, because
    /// this is the place that knows what the field is for. Past this point a value is a shape: the
    /// engine and the file both take it as it is.
    /// </summary>
    private ExpressionNode WrittenValue() => FormulaText.Read(CurrentText.Trim(), Definition.ReadMode);

    /// <summary>Loads a value that was stored on an existing step.</summary>
    public void ApplyValue(StepParameter stored)
    {
        if (Definition.Kind is ActionParameterKind.Steps)
        {
            List?.Load(stored.Steps);
            return;
        }

        if (Definition.Kind is ActionParameterKind.Region)
        {
            Regions.Clear();
            foreach (var row in stored.Rows)
            {
                AddRegion(RegionRowViewModel.From(row));
            }

            return;
        }

        if (Definition.Kind is ActionParameterKind.ColourPoints)
        {
            ColourPointRows.Clear();
            foreach (var row in stored.Rows)
            {
                AddColourPoint(ColourPointRowViewModel.From(row));
            }

            return;
        }

        if (Definition.Kind is ActionParameterKind.KeySequence)
        {
            KeyRows.Clear();
            foreach (var row in stored.Rows)
            {
                AddKeyRow(KeyRowViewModel.From(row));
            }

            return;
        }

        if (Definition.Kind is ActionParameterKind.Images)
        {
            Pictures.Clear();
            foreach (var row in stored.Rows)
            {
                AddPicture(ImageRowViewModel.From(row));
            }

            return;
        }

        if (Definition.Kind is ActionParameterKind.Narrow)
        {
            NarrowRows.Clear();
            foreach (var row in stored.Rows)
            {
                AddNarrowRow(NarrowRowViewModel.From(row, Definition.NarrowFamily));
            }

            return;
        }

        if (Definition.Kind is ActionParameterKind.Returns)
        {
            // What the file does not name is a result this step did not hand over, so every line
            // starts unticked and only the ones that were written are ticked back on.
            foreach (var row in ReturnsRows)
            {
                row.Skip();
            }

            foreach (var written in stored.Rows)
            {
                var key = written.Text(FieldNames.Key).Trim();
                var name = written.Text(FieldNames.Name).Trim();
                var match = ReturnsRows.FirstOrDefault(row =>
                    string.Equals(row.Key, key, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                {
                    // 这一步早就勾过它，而它已经不在列表上了：不画，但保存时原样写回去。
                    if (name.Length > 0)
                    {
                        _keptResults[key] = name;
                    }

                    continue;
                }

                match.Load(name);
            }

            OnPropertyChanged(nameof(HandsOverAnything));
            return;
        }

        JitterPercent = stored.HasJitter ? stored.Jitter * 100m : null;

        var raw = stored.Value;
        switch (Definition.Kind)
        {
            case ActionParameterKind.Number:
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    UseFormula = false;
                    ShowMilliseconds(number);
                }
                else
                {
                    // Anything that is not a plain number is an expression the engine can still
                    // evaluate, so the text has to survive opening and saving the step.
                    UseFormula = true;
                    Text = raw;
                }
                break;
            case ActionParameterKind.Bool:
                Flag = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case ActionParameterKind.Choice:
                Option = Choices.FirstOrDefault(choice => choice.Value == raw) ?? Choices.FirstOrDefault();
                break;
            case ActionParameterKind.Step:
                {
                    // A name that is not one of the macro's steps — pointing at a step that has
                    // since been deleted, or written by hand into the file — is kept and shown as
                    // itself, rather than being quietly swapped for whichever step is first.
                    var step = Choices.FirstOrDefault(choice => choice.Value == raw);
                    if (step is null && raw.Length > 0)
                    {
                        step = new ActionParameterOption(raw, Strings.Format("Add.StepNotInMacro", raw));
                        Choices.Add(step);
                    }

                    Option = step;
                }

                break;
            default:
                Text = raw;
                break;
        }
    }

    private static ActionParameterOption? PickInitialOption(
        IReadOnlyList<ActionParameterOption> choices, string defaultValue)
    {
        if (choices.Count == 0)
        {
            return null;
        }

        return choices.FirstOrDefault(choice => choice.Value == defaultValue) ?? choices[0];
    }
}
