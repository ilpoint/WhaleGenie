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

// The variables panel: which values to show, where they come from and what kind they are.
public partial class RunViewModel
{
    private void RefreshVariables()
    {
        Variables.Clear();

        // Every variable the macro uses is listed from the start, whether or not a step has reached
        // it: a watch list that grows one row at a time answers "what has run", not "what does this
        // macro work with". The value is empty until something writes it.
        var live = _variables.Flatten().ToDictionary(pair => pair.Key, pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);
        var rows = _choices
            .Where(choice => _watched.Contains(choice.Name))
            .Select(choice => new WatchedVariable(choice.Name, ScopeLabel(choice.Scope),
                TypeOf(choice.Type, live.GetValueOrDefault(choice.Name)),
                live.TryGetValue(choice.Name, out var held) ? held.AsText() : string.Empty))
            .OrderBy(row => Rank(row.Name))
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            Variables.Add(row);
        }

        RefreshStepOutcomes();
        OnPropertyChanged(nameof(HasVariables));
    }

    /// <summary>
    /// Reads back how each step went. Only the steps of this macro are asked about, and a step
    /// counts as answered only once the run has written something for it: before the run starts
    /// the list is empty rather than a column of "nothing yet".
    /// </summary>
    private void RefreshStepOutcomes()
    {
        StepOutcomes.Clear();
        foreach (var step in Steps)
        {
            if (step.Id.Length == 0)
            {
                continue;
            }

            // The step the run stands in front of has not been carried out yet, so it has nothing
            // to report — even though a value for it was written before the run began.
            if (!_reached.Contains(step.Id) || step.Id == CurrentId)
            {
                continue;
            }

            var name = MacroRunner.OutcomeName(step.Id);
            if (_variables.Local.TryGet(name, out var value))
            {
                StepOutcomes.Add(new StepOutcome(name, step.Label, value.AsText()));
            }
        }

        OnPropertyChanged(nameof(HasStepOutcomes));
        OnPropertyChanged(nameof(StepOutcomeHeader));
    }

    private int Rank(string name) => _variables.Local.Contains(name) ? 0
        : _variables.Global.Contains(name) ? 1
        : 2;

    private string ScopeOf(string name) => _variables.Local.Contains(name)
        ? Strings.Get("Variable.Scope.Local")
        : _variables.Global.Contains(name)
            ? Strings.Get("Variable.Scope.Global")
            : Strings.Get("Variable.Scope.System");

    /// <summary>Where a variable lives, as the one word the watch list shows for it.</summary>
    private static string ScopeLabel(VariableScope scope) => scope switch
    {
        VariableScope.Local => Strings.Get("Variable.Scope.Local"),
        VariableScope.Global => Strings.Get("Variable.Scope.Global"),
        _ => Strings.Get("Variable.Scope.System"),
    };

    /// <summary>
    /// What a watched variable holds, in the catalogue's own words. A variable a step creates says
    /// nothing about its type until something is written, so the value that is there answers
    /// instead; with neither, the type is left blank rather than guessed.
    /// </summary>
    private static string TypeOf(string declared, Value? held)
    {
        if (declared.Length > 0)
        {
            return declared;
        }

        return held switch
        {
            null => string.Empty,
            { IsList: true } => "list",
            { IsRecord: true } => "record",
            { IsNumber: true } => "number",
            _ => "text",
        };
    }

    /// <summary>
    /// The names worth watching: everything the macro creates for itself, plus everything it
    /// reads. Anything else stays out of the list.
    /// </summary>
    private static HashSet<string> VariablesUsed(IReadOnlyList<MacroStep> steps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            step.CollectVariables(defined);
        }

        // The macro's own variables are worth watching even before a step sets them.
        names.UnionWith(defined);

        foreach (var step in steps)
        {
            CollectUses(step, defined, names);
        }

        return names;
    }

    private static void CollectUses(MacroStep step, HashSet<string> defined, HashSet<string> names)
    {
        foreach (var parameter in step.Parameters)
        {
            switch (parameter.Kind)
            {
                case ActionParameterKind.Steps:
                    foreach (var child in parameter.Steps)
                    {
                        CollectUses(child, defined, names);
                    }

                    break;
                default:
                    // A list parameter keeps its values in rows, and a row may name the variable
                    // that says where to look — a picture an earlier Capture took, most often.
                    foreach (var value in parameter.IsRowList
                        ? parameter.RowValues.Select(FormulaText.Write)
                        : [parameter.Value])
                    {
                        Note(value, defined, names);
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Reads a field the way the runner would: a bare variable name, or <c>$name</c> mentioned
    /// inside a longer piece of text or an expression.
    /// </summary>
    private static void Note(string text, HashSet<string> defined, HashSet<string> names)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (defined.Contains(trimmed) || VariableCatalog.IsSystem(trimmed) || VariableCatalog.IsGlobal(trimmed))
        {
            names.Add(trimmed);
        }

        foreach (var name in WhaleGenie.Core.Expressions.Expression.ReferencedNames(trimmed))
        {
            names.Add(name);
        }
    }

    // ------------------------------------------------------------------- variables

    /// <summary>Built-in variables, with the value they hold right now.</summary>
    public ObservableCollection<VariableDefinition> SystemVariables { get; } = [];

    /// <summary>Every variable the macros in the list use: global, then local, then built-in.</summary>
    public ObservableCollection<VariableDefinition> CurrentVariables { get; } = [];

    /// <summary>The global declarations and their current values, shown read-only while debugging.</summary>
    public ObservableCollection<RunGlobalVariable> Globals { get; } = [];

    public bool HasGlobals => Globals.Count > 0;

    /// <summary>Variables created inside each macro in the macro list.</summary>
    public ObservableCollection<MacroVariableGroup> LocalVariables { get; } = [];

    public bool HasLocalVariables => LocalVariables.Count > 0;

    /// <summary>Which set of variables is listed: the ones in use, or one scope on its own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentTab))]
    [NotifyPropertyChangedFor(nameof(IsLocalTab))]
    [NotifyPropertyChangedFor(nameof(IsGlobalTab))]
    [NotifyPropertyChangedFor(nameof(IsSystemTab))]
    public partial VariableTab SelectedTab { get; set; } = VariableTab.Current;

    public bool IsCurrentTab => SelectedTab is VariableTab.Current;

    public bool IsLocalTab => SelectedTab is VariableTab.Local;

    public bool IsGlobalTab => SelectedTab is VariableTab.Global;

    public bool IsSystemTab => SelectedTab is VariableTab.System;
}
