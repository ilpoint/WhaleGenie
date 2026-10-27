using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The little panel a breakpoint's condition is written in: one expression, one count, and the
/// names the run knows, offered so a condition can be built instead of remembered. It is not the
/// action editor: a condition is a question about the run, not a step of the macro.
/// </summary>
public sealed partial class BreakpointViewModel : ObservableObject
{
    /// <summary>What the panel was closed with.</summary>
    public enum Ending
    {
        Save,
        Clear,
        Cancel,
    }

    public BreakpointViewModel(BreakpointRule? current, IReadOnlyList<VariableChoice> variables)
    {
        Expression = current?.Expression ?? string.Empty;
        HitTimes = current?.HitTimes ?? 1;
        Variables = [.. variables.Select(choice => choice.Name).OrderBy(name => name,
            StringComparer.OrdinalIgnoreCase)];
    }

    [ObservableProperty]
    public partial string Expression { get; set; } = string.Empty;

    /// <summary>
    /// How many arrivals count before the run stops. Kept as a decimal because the box that edits it
    /// is one, and turned back into a count when the panel is saved.
    /// </summary>
    [ObservableProperty]
    public partial decimal HitTimes { get; set; } = 1;

    /// <summary>The names a condition may use, which are the ones the macro's own fields offer.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>Set from the picker to put a name into the expression.</summary>
    [ObservableProperty]
    public partial string? ChosenVariable { get; set; }

    /// <summary>What the panel was closed with.</summary>
    public Ending How { get; private set; } = Ending.Cancel;

    /// <summary>The rule to keep, or nothing when the breakpoint was put back to stopping always.</summary>
    public BreakpointRule? Rule => How is Ending.Save
        ? new BreakpointRule
        {
            Expression = Expression.Trim(),
            HitTimes = Math.Max(1, (int)HitTimes),
        }
        : null;

    /// <summary>Raised once the panel is finished, whichever way it ended.</summary>
    public event Action? Finished;

    partial void OnChosenVariableChanged(string? value)
    {
        if (value is not { Length: > 0 })
        {
            return;
        }

        var expression = Expression.TrimEnd();
        Expression = expression.Length == 0 ? $"${value}" : $"{expression} ${value}";
        ChosenVariable = null;
    }

    [RelayCommand]
    private void Save()
    {
        How = Ending.Save;
        Finished?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        How = Ending.Clear;
        Finished?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        How = Ending.Cancel;
        Finished?.Invoke();
    }
}
