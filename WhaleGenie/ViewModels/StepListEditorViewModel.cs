using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Edits a list of child steps inside an action form.
/// </summary>
public partial class StepListEditorViewModel : ViewModelBase
{
    /// <summary>Creates an editor for a step list.</summary>
    public StepListEditorViewModel(string addLabel, IReadOnlyList<ActionDefinition>? catalog = null)
    {
        AddLabel = addLabel;
        Catalog = catalog;

        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(HasNoSteps));
    }

    /// <summary>Raised when the dialog should open the picker to edit a step.</summary>
    public event Action<MacroStep>? EditRequested;

    public ObservableCollection<MacroStep> Steps { get; } = [];

    /// <summary>Restricts the picker to a catalogue subset, or <c>null</c> for everything.</summary>
    public IReadOnlyList<ActionDefinition>? Catalog { get; }

    /// <summary>Label of the button that opens the picker.</summary>
    public string AddLabel { get; }

    public bool HasSteps => Steps.Count > 0;

    public bool HasNoSteps => !HasSteps;

    /// <summary>True when there is a condition picked to edit.</summary>
    public bool CanEdit => SelectedStep is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    public partial MacroStep? SelectedStep { get; set; }

    [RelayCommand]
    private void Add()
    {
        // Child steps are inserted in the macro's main list at the chosen drop slot. The action
        // form only reports where the block is; it is not a second editor for its children.
    }

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
        OnPropertyChanged(nameof(HasNoSteps));
        OnPropertyChanged(nameof(Steps));
        OnPropertyChanged(nameof(CanEdit));
    }
}
