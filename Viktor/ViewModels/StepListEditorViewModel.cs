using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Viktor.Models;

namespace Viktor.ViewModels;

/// <summary>
/// Edits the condition of a step: the one an if, a while or a wait tests, or the conditions a
/// logic group combines. This is the one thing a step needs that is not a step itself, which is
/// why it is picked in the dialog rather than written in the editor's step list.
/// </summary>
public partial class StepListEditorViewModel : ViewModelBase
{
    /// <summary>Creates an editor for a step list, or for a single condition.</summary>
    public StepListEditorViewModel(string addLabel, bool isCondition,
        IReadOnlyList<ActionDefinition>? catalog = null)
    {
        AddLabel = addLabel;
        IsCondition = isCondition;
        Catalog = catalog;

        OnPropertyChanged(nameof(HasSteps));
    }

    /// <summary>Raised when the dialog should open the picker to add a step.</summary>
    public event Action<StepListEditorViewModel>? AddRequested;

    /// <summary>Raised when the dialog should open the picker to edit a step.</summary>
    public event Action<MacroStep>? EditRequested;

    public ObservableCollection<MacroStep> Steps { get; } = [];

    /// <summary>Restricts the picker to a catalogue subset, or <c>null</c> for everything.</summary>
    public IReadOnlyList<ActionDefinition>? Catalog { get; }

    /// <summary>Label of the button that opens the picker.</summary>
    public string AddLabel { get; }

    /// <summary>True when this editor holds one condition rather than a list of steps.</summary>
    public bool IsCondition { get; }

    public bool HasSteps => Steps.Count > 0;

    /// <summary>True when there is a condition picked to edit.</summary>
    public bool CanEdit => SelectedStep is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial MacroStep? SelectedStep { get; set; }

    [RelayCommand]
    private void Add() => AddRequested?.Invoke(this);

    [RelayCommand]
    private void Edit()
    {
        if (SelectedStep is not null)
        {
            EditRequested?.Invoke(SelectedStep);
        }
    }

    /// <summary>Adds a step chosen in the picker.</summary>
    public void AddStep(MacroStep step)
    {
        if (IsCondition)
        {
            // A condition is a single expression, so picking a new one replaces the old.
            Steps.Clear();
        }

        Steps.Add(step);
        SelectedStep = step;
        Refresh();
    }

    /// <summary>Swaps a step for the edited version returned by the picker.</summary>
    public void ReplaceStep(MacroStep original, MacroStep replacement)
    {
        var index = Steps.IndexOf(original);
        if (index < 0)
        {
            return;
        }

        Steps[index] = replacement;
        SelectedStep = replacement;
        Refresh();
    }

    /// <summary>Replaces the whole list, used when loading an existing step.</summary>
    public void Load(IEnumerable<MacroStep> steps)
    {
        Steps.Clear();
        foreach (var step in steps)
        {
            Steps.Add(step);
        }

        SelectedStep = Steps.FirstOrDefault();
        Refresh();
    }

    /// <summary>Signals the owning dialog that the list on screen has changed.</summary>
    private void Refresh()
    {
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(CanEdit));
    }
}
