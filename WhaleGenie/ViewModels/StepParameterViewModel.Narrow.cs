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

// The conditions that narrow a browser search to one element.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>Puts another thing the element has to be like at the end of the list.</summary>
    public void AddNarrowRow(NarrowRowViewModel? row = null)
    {
        var added = row ?? new NarrowRowViewModel(Definition.NarrowFamily);
        added.Take = RemoveNarrowRow;
        NarrowRows.Add(added);
        OnPropertyChanged(nameof(HasNarrowRows));
    }

    /// <summary>Takes one of them out of the list.</summary>
    public void RemoveNarrowRow(NarrowRowViewModel row)
    {
        NarrowRows.Remove(row);
        OnPropertyChanged(nameof(HasNarrowRows));
    }

    /// <summary>
    /// The things the element has to be like, the way the engine reads them: rows that say nothing
    /// are left out, and with <paramref name="upto"/> named the rows after that one are left out as
    /// well, which is how one row is looked at on top of the rows above it.
    /// </summary>
    public IReadOnlyList<NarrowCondition> Conditions(NarrowRowViewModel? upto = null)
    {
        var conditions = new List<NarrowCondition>();
        foreach (var row in NarrowRows)
        {
            if (row.ToCondition() is { } condition)
            {
                conditions.Add(condition);
            }

            if (ReferenceEquals(row, upto))
            {
                break;
            }
        }

        return conditions;
    }

    /// <summary>
    /// The results this action can hand over: one line each, with a box saying whether the step
    /// keeps it and the name it goes by. Only the ticked ones are written, so a step that reports
    /// eight things does not put eight variables in front of somebody who wanted one.
    /// </summary>
    public ObservableCollection<ReturnsRowViewModel> ReturnsRows { get; } = [];

    /// <summary>True while the step hands anything over at all.</summary>
    public bool HandsOverAnything => ReturnsRows.Any(row => row.ToRow() is not null)
        || _keptResults.Count > 0;

    /// <summary>
    /// Follows one line of the returns list: a tick or a name written on it is a change to this
    /// field, and the dialog decides from the field whether the step hands anything over at all.
    /// Only those two: the row also reports its own warnings, and those are answers to what this
    /// field says rather than changes to it.
    /// </summary>
    private ReturnsRowViewModel Watch(ReturnsRowViewModel row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ReturnsRowViewModel.Ticked)
                or nameof(ReturnsRowViewModel.Name))
            {
                OnPropertyChanged(nameof(HandsOverAnything));
            }
        };

        return row;
    }

    /// <summary>
    /// The results a step handed over that are no longer offered, as they were read. They are not
    /// drawn and cannot be changed here; they are simply written back as they came.
    /// </summary>
    private IEnumerable<StepParameterRow> Kept() => _keptResults.Select(kept => new StepParameterRow
    {
        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
        {
            [FieldNames.Key] = Formula.Text(kept.Key),
            [FieldNames.Name] = Formula.Text(kept.Value),
        },
    });

    /// <summary>
    /// Leaves every result unticked: a step that was saved handing nothing over hands nothing over
    /// when it is opened again, and an action that has just been chosen starts from its catalogue
    /// defaults instead.
    /// </summary>
    public void ClearReturns()
    {
        foreach (var row in ReturnsRows)
        {
            row.Skip();
        }

        OnPropertyChanged(nameof(HandsOverAnything));
    }

    /// <summary>
    /// Names every result the step hands over, following the note for as long as the name has not
    /// been chosen by hand. Called when the note changes, and once when the dialog is built.
    /// </summary>
    public void SettleReturns(string note, string stepId, IReadOnlyList<string> taken)
    {
        var spoken = new List<string>(taken);
        foreach (var row in ReturnsRows)
        {
            row.Knows(spoken);
            if (row.Ticked)
            {
                row.Suggest(VariableNames.ForStep(note, row.Label, stepId, spoken));
            }

            if (row.Name.Trim() is { Length: > 0 } named)
            {
                spoken.Add(named);
            }
        }
    }
}
