using System.ComponentModel;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One line of the action dialog's parameter list. The two halves of a screen position share a
/// line, so x and y sit side by side instead of leaving two half-empty rows. A line may also carry
/// one field that belongs beside the others rather than under them, such as the switch that takes a
/// mouse step's place from the pointer: it is read together with the field it stands next to.
/// </summary>
public sealed class ParameterRowViewModel : ViewModelBase
{
    public ParameterRowViewModel(StepParameterViewModel first, StepParameterViewModel? second = null,
        StepParameterViewModel? third = null)
    {
        First = first;
        Second = second;
        Third = third;

        // Whether a line is drawn is a question about the step's settings rather than about the
        // line, so it is asked again whenever one of the fields on it has something new to say:
        // without this the form goes on showing a field the step no longer asks about until it is
        // built again, which for a step being written is never.
        foreach (var field in new[] { first, second, third })
        {
            if (field is not null)
            {
                field.PropertyChanged += OnFieldChanged;
            }
        }
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs args)
    {
        OnPropertyChanged(nameof(IsApplicable));
        OnPropertyChanged(nameof(IsDefault));
    }

    public StepParameterViewModel First { get; }

    public StepParameterViewModel? Second { get; }

    /// <summary>The field that belongs beside this line rather than on one of its own.</summary>
    public StepParameterViewModel? Third { get; }

    public bool HasSecond => Second is not null;

    public bool HasThird => Third is not null;

    /// <summary>
    /// False while what this line asks about is not a question for the way the step is set up.
    /// Such a line is not drawn at all, rather than being drawn with nothing behind it.
    /// </summary>
    public bool IsApplicable => First.IsApplicable;

    /// <summary>
    /// True while both halves of the line still hold what they started with, which is what the
    /// mark on a page's tab is read from: a page holding a changed value is worth pointing at.
    /// </summary>
    public bool IsDefault => First.IsDefault && (Second?.IsDefault ?? true)
        && (Third?.IsDefault ?? true);

    /// <summary>
    /// True when this line carries the button that picks a whole screen region. A field that holds
    /// one rectangle has that button on the rectangle itself, so the line under it does not repeat
    /// it; a search that may look in several places has no single row to hang it on, and the button
    /// that adds a picked place belongs under the list.
    /// </summary>
    public bool IsRegionAnchor => First.IsRegionAnchor;

    /// <summary>True when this line carries the button that picks a click point on the picture.</summary>
    public bool ShowsOffsetPick => First.IsOffsetAnchor;

    /// <summary>True when the line says a position can be taken from the pointer with Alt + X.</summary>
    public bool ShowsPositionHint => HasSecond && First.IsCoordinate;

    /// <summary>Half the line beside another editor, the whole line when it is on its own.</summary>
    public int FirstSpan => HasSecond ? 1 : 2;
}
