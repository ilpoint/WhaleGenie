namespace Viktor.ViewModels;

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

    /// <summary>Half the line beside another editor, the whole line when it is on its own.</summary>
    public int FirstSpan => HasSecond ? 1 : 2;
}
