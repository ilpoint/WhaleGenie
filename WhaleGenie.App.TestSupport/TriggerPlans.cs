using System.Collections.Generic;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests.Support;

/// <summary>
/// Builds the plan a trigger service watches: the macros a check made, and the entries that start
/// them. A check about a key press says so by adding a <see cref="KeySpec"/> rather than by
/// filling in fields on the macro.
/// </summary>
public sealed class TriggerPlanBuilder
{
    private readonly List<MacroItem> _macros = [];
    private readonly ProjectTriggers _triggers = new();
    private string _menuLauncher = string.Empty;

    /// <summary>Adds a macro and the triggers that start it.</summary>
    public TriggerPlanBuilder Macro(MacroItem macro, params TriggerSpec[] specs)
    {
        _macros.Add(macro);
        foreach (var spec in specs)
        {
            _triggers.Entries.Add(new TriggerEntry { MacroName = macro.Name, Spec = spec });
        }

        return this;
    }

    /// <summary>Adds a macro with no trigger, for a check that starts it by hand.</summary>
    public TriggerPlanBuilder Macro(MacroItem macro)
    {
        _macros.Add(macro);
        return this;
    }

    /// <summary>Adds one more trigger to the last macro, for a check about two of them.</summary>
    public TriggerPlanBuilder Also(TriggerSpec spec)
    {
        _triggers.Entries.Add(new TriggerEntry { MacroName = _macros[^1].Name, Spec = spec });
        return this;
    }

    public TriggerPlanBuilder Menu(string binding)
    {
        _menuLauncher = binding;
        return this;
    }

    public TriggerPlan Build() => TriggerPlan.From(_triggers, _macros, _menuLauncher);
}

/// <summary>The way a check asks for a plan.</summary>
public static class TriggerPlans
{
    public static TriggerPlanBuilder Build() => new();

    /// <summary>A plan with one macro and the triggers given.</summary>
    public static TriggerPlan For(MacroItem macro, params TriggerSpec[] specs)
        => Build().Macro(macro, specs).Build();
}
