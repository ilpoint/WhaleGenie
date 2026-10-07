namespace WhaleGenie.ViewModels;

/// <summary>
/// One line of the action dialog's parameter list. The two halves of a screen position share a
/// line, so x and y sit side by side instead of leaving two half-empty rows.
/// </summary>
public sealed class ParameterRowViewModel
{
    public ParameterRowViewModel(StepParameterViewModel first, StepParameterViewModel? second = null)
    {
        First = first;
        Second = second;
    }

    public StepParameterViewModel First { get; }

    public StepParameterViewModel? Second { get; }

    public bool HasSecond => Second is not null;

    /// <summary>True when this line carries the button that picks a whole screen region.</summary>
    public bool IsRegionAnchor => First.IsRegionAnchor;

    /// <summary>True when the line says a position can be taken from the pointer with Alt + X.</summary>
    public bool ShowsPositionHint => HasSecond && First.IsCoordinate;

    /// <summary>Half the line beside another editor, the whole line when it is on its own.</summary>
    public int FirstSpan => HasSecond ? 1 : 2;
}
