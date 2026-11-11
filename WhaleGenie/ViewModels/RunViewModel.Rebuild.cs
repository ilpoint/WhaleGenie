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

// Rebuilding the list after an edit, and the names a macro uses that are not its own.
public partial class RunViewModel
{
    /// <summary>Whether the "in use" list also shows the built-in variables a macro touched.</summary>
    [ObservableProperty]
    public partial bool ShowSystemVariables { get; set; }

    public bool HasCurrentVariables => CurrentVariables.Count > 0;

    partial void OnShowSystemVariablesChanged(bool value) => RebuildCurrent();

    [RelayCommand]
    private void SelectTab(string? tab) => SelectedTab = tab switch
    {
        nameof(VariableTab.Local) => VariableTab.Local,
        nameof(VariableTab.Global) => VariableTab.Global,
        nameof(VariableTab.System) => VariableTab.System,
        _ => VariableTab.Current,
    };

    /// <summary>Reads the built-in variables again and re-lists the macro variables.</summary>
    public void Rebuild()
    {
        SystemVariables.Clear();

        Globals.Clear();
        foreach (var shared in VariableCatalog.Globals)
        {
            Globals.Add(new RunGlobalVariable
            {
                Name = shared.Name,
                Type = shared.Type,
                DefaultValue = shared.DefaultValue,
                Description = shared.Description,
                PersistValue = shared.PersistValue,
                CurrentValue = _variables.TryGet(shared.Name, out var value)
                    ? value.AsText()
                    : String.IsNullOrWhiteSpace(shared.DefaultValue)
                        ? Strings.Get("Variable.NotSet")
                        : VariableDefinition.Preview(shared.Type, shared.DefaultValue),
            });
        }

        foreach (var variable in VariableCatalog.SystemVariables)
        {
            var row = new VariableDefinition
            {
                Name = variable.Name,
                Scope = variable.Scope,
                Type = variable.Type,
                Description = variable.Description,
                DefaultValue = variable.DefaultValue,
            };

            // What a macro would read right now, which is what this page is about: the value a step
            // has left in place comes first — the round of a loop, the macro being run — and behind it
            // the ones read off the machine as they are asked for.
            row.CurrentValue = _variables.TryGet(variable.Name, out var value)
                ? value.AsText()
                : _system.Read(variable.Name);

            SystemVariables.Add(row);
        }

        LocalVariables.Clear();
        foreach (var macro in _listed)
        {
            var names = macro.LocalVariableNames();
            if (names.Count == 0)
            {
                continue;
            }

            LocalVariables.Add(new MacroVariableGroup
            {
                MacroName = macro.Name,
                Variables =
                [
                    .. names.Select(name => new VariableDefinition
                    {
                        Name = name,
                        Scope = VariableScope.Local,
                        Owner = macro.Name,
                        Description = Strings.Get("Variable.LocalDescription"),
                        CurrentValue = Strings.Get("Variable.NotSet"),
                    }),
                ],
            });
        }

        OnPropertyChanged(nameof(HasLocalVariables));
        RebuildCurrent();
    }

    /// <summary>
    /// Lists the variables the macros in the list actually use, with the scope and the macros that
    /// use them, so their state can be checked before a macro runs. A name a macro uses but no step
    /// defines is called out, which is usually a typo.
    /// </summary>
    private void RebuildCurrent()
    {
        CurrentVariables.Clear();

        var system = VariableCatalog.SystemVariables
            .ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var globals = VariableCatalog.Globals
            .ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var live = SystemVariables
            .ToDictionary(variable => variable.Name, variable => variable.CurrentValue,
                StringComparer.OrdinalIgnoreCase);

        var used = new Dictionary<string, Usage>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in _listed)
        {
            var locals = macro.LocalVariableNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(system.Keys, StringComparer.OrdinalIgnoreCase);
            known.UnionWith(globals.Keys);
            known.UnionWith(locals);

            foreach (var name in UsedNames(macro, known))
            {
                var scope = locals.Contains(name) ? VariableScope.Local
                    : globals.ContainsKey(name) ? VariableScope.Global
                    : system.ContainsKey(name) ? VariableScope.System
                    : VariableScope.Unknown;

                if (scope is VariableScope.System && !ShowSystemVariables)
                {
                    continue;
                }

                // Two macros can each own a local variable with the same name.
                var key = scope is VariableScope.Local ? $"{name}\u0000{macro.Name}" : name;
                if (!used.TryGetValue(key, out var usage))
                {
                    usage = new Usage(name, scope);
                    used[key] = usage;
                }

                usage.Owners.Add(macro.Name);
            }
        }

        foreach (var usage in used.Values
                     .OrderBy(entry => RankOf(entry.Scope))
                     .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new VariableDefinition
            {
                Name = usage.Name,
                Scope = usage.Scope,
                Owner = string.Join(", ", usage.Owners),
                Description = DescriptionOf(usage, system, globals),
                Type = TypeOf(usage, system, globals),
                CurrentValue = ValueOf(usage, system, globals, live),
            };

            CurrentVariables.Add(row);
        }

        OnPropertyChanged(nameof(HasCurrentVariables));
    }

    /// <summary>Which variables a macro reads: the ones it defines, plus the ones it names.</summary>
    private static SortedSet<string> UsedNames(MacroItem macro, IReadOnlySet<string> known)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in macro.Steps)
        {
            Collect(step);
        }

        return names;

        void Collect(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                switch (parameter.Kind)
                {
                    case ActionParameterKind.Steps:
                        foreach (var child in parameter.Steps)
                        {
                            Collect(child);
                        }

                        break;
                    default:
                        // A list parameter keeps its values in rows: whichever of them names a
                        // variable is one the macro reads, the same as any plain field that starts
                        // with a "$".
                        foreach (var value in parameter.IsRowList
                            ? parameter.RowValues.Select(FormulaText.Write)
                            : [parameter.Value])
                        {
                            Plain(step, parameter, value);
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// One value the macro reads: a whole-value expression may name several variables anywhere
        /// inside it, a plain field names one when it starts with a "$", and a field that always
        /// names a variable counts even when nothing defines it — which is worth showing while
        /// debugging.
        /// </summary>
        void Plain(MacroStep step, StepParameter parameter, string value)
        {
            var text = value.Trim();
            if (text.Length == 0)
            {
                return;
            }

            if (parameter.Kind is ActionParameterKind.Expression)
            {
                foreach (var reference in Expression.ReferencedNames(text))
                {
                    names.Add(reference);
                }

                return;
            }

            if (text.StartsWith('$'))
            {
                var reference = text[1..].Trim();
                if (reference.Length > 0)
                {
                    names.Add(reference);
                }

                return;
            }

            if (known.Contains(text))
            {
                names.Add(text);
                return;
            }

            // A parameter that always names a variable can point at a name nothing defines, which
            // is worth showing while debugging.
            var declares = step.Definition?.Parameters
                .FirstOrDefault(candidate => candidate.Name == parameter.Name)?.NamesVariable;
            if (declares == true)
            {
                names.Add(text);
            }
        }
    }

    private static int RankOf(VariableScope scope) => scope switch
    {
        VariableScope.Global => 0,
        VariableScope.Local => 1,
        VariableScope.System => 2,
        _ => 3,
    };

    private static string DescriptionOf(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals) => usage.Scope switch
        {
            VariableScope.System when system.TryGetValue(usage.Name, out var builtIn) => builtIn.LocalDescription,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared) => shared.Description,
            VariableScope.Local => Strings.Get("Variable.LocalDescription"),
            _ => Strings.Get("Variable.UnknownHint"),
        };

    private static string TypeOf(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals)
        => usage.Scope switch
        {
            VariableScope.System when system.TryGetValue(usage.Name, out var builtIn) => builtIn.Type,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared) => shared.Type,
            _ => "text",
        };

    private static string ValueOf(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals,
        IReadOnlyDictionary<string, string> live)
        => usage.Scope switch
        {
            VariableScope.System when live.TryGetValue(usage.Name, out var current) => current,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared)
                && shared.DefaultValue.Length > 0 => VariableDefinition.Preview(shared.Type, shared.DefaultValue),
            _ => Strings.Get("Variable.NotSet"),
        };

    /// <summary>One variable in use, together with the macros that use it.</summary>
    private sealed class Usage(string name, VariableScope scope)
    {
        public string Name { get; } = name;

        public VariableScope Scope { get; } = scope;

        public SortedSet<string> Owners { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    [RelayCommand]
    private void Refresh() => Rebuild();
}
