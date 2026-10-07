using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>The three pages of the Variable Center.</summary>
public enum VariableTab
{
    /// <summary>Everything the macros in the list can use right now.</summary>
    Current,

    /// <summary>Only the variables a macro created for itself.</summary>
    Local,

    /// <summary>Only the shared variables, which are edited here.</summary>
    Global,

    /// <summary>Only the variables WhaleGenie provides.</summary>
    System,
}

/// <summary>The local variables one macro created, grouped under the macro's name.</summary>
public sealed class MacroVariableGroup
{
    public required string MacroName { get; init; }

    public required IReadOnlyList<VariableDefinition> Variables { get; init; }
}

/// <summary>
/// Backs the Variable Center. It shows the live value of every built-in variable, lets the
/// user manage the shared global variables, and lists the local variables of the macros
/// currently in the macro list.
/// </summary>
public partial class VariableCenterViewModel : ViewModelBase
{
    private readonly IReadOnlyList<MacroItem> _macros;
    private readonly int _screenWidth;
    private readonly int _screenHeight;
    private string _clipboard = string.Empty;
    private readonly List<VariableDefinition> _clipboardRows = [];

    public VariableCenterViewModel()
        : this(null)
    {
    }

    public VariableCenterViewModel(IEnumerable<MacroItem>? macros,
        int screenWidth = 0, int screenHeight = 0)
    {
        NewType = Types[0];
        _macros = [.. macros ?? []];
        _screenWidth = screenWidth;
        _screenHeight = screenHeight;

        Globals.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGlobals));
        Rebuild();
    }

    /// <summary>Built-in variables, with the value they hold right now.</summary>
    public ObservableCollection<VariableDefinition> SystemVariables { get; } = [];

    /// <summary>Every variable in use: global, then local, then the built-in ones.</summary>
    public ObservableCollection<VariableDefinition> CurrentVariables { get; } = [];

    /// <summary>Shared variables. Only this window may create them.</summary>
    public ObservableCollection<VariableDefinition> Globals => VariableCatalog.Globals;

    public bool HasGlobals => Globals.Count > 0;

    /// <summary>Variables created inside each macro in the macro list.</summary>
    public ObservableCollection<MacroVariableGroup> MacroVariables { get; } = [];

    public bool HasMacroVariables => MacroVariables.Count > 0;

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

    /// <summary>Whether the "in use" page also lists the built-in variables a macro touched.</summary>
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

    public IReadOnlyList<ActionParameterOption> Types { get; } =
    [
        .. VariableDefinition.TypeNames.Select(name =>
            new ActionParameterOption(name, Strings.Get($"Variable.Type.{name}", name))),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalCommand))]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTypeIsList))]
    [NotifyPropertyChangedFor(nameof(NewDefaultPreview))]
    [NotifyPropertyChangedFor(nameof(NewDefaultHasError))]
    [NotifyPropertyChangedFor(nameof(ShowNewDefaultSummary))]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalCommand))]
    public partial ActionParameterOption NewType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewDefaultPreview))]
    [NotifyPropertyChangedFor(nameof(NewDefaultHasError))]
    [NotifyPropertyChangedFor(nameof(ShowNewDefaultSummary))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalCommand))]
    public partial string NewDefault { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewDescription { get; set; } = string.Empty;

    /// <summary>Explains why a new global variable cannot be added yet.</summary>
    public string ValidationMessage
    {
        get
        {
            var name = NewName.Trim();
            if (name.Length == 0)
            {
                return string.Empty;
            }

            if (VariableCatalog.IsSystem(name))
            {
                return Strings.Format("Variable.NameIsSystem", name);
            }

            if (VariableCatalog.IsGlobal(name))
            {
                return Strings.Format("Variable.NameTaken", name);
            }

            return NewDefaultHasError ? Strings.Get("Variable.InvalidDefault") : string.Empty;
        }
    }

    public bool CanAdd => NewName.Trim().Length > 0 && ValidationMessage.Length == 0;

    /// <summary>True while the type chosen for a new global holds a list.</summary>
    public bool NewTypeIsList => NewType.Value is "list";

    /// <summary>How the starting value of the new global reads, showing what a list holds.</summary>
    public string NewDefaultPreview => VariableDefinition.Preview(NewType.Value, NewDefault);

    /// <summary>True while the starting value of the new global cannot be read.</summary>
    public bool NewDefaultHasError => VariableDefinition.HasError(NewType.Value, NewDefault);

    /// <summary>True while the new global's preview shows what a list holds.</summary>
    public bool ShowNewDefaultSummary => NewTypeIsList && !NewDefaultHasError;

    /// <summary>Reads the built-in variables again and re-lists the macro variables.</summary>
    public void Rebuild()
    {
        SystemVariables.Clear();
        _clipboardRows.Clear();

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

            row.CurrentValue = variable.Name == "sys.clipboard"
                ? _clipboard
                : SystemVariableReader.Read(variable.Name, _screenWidth, _screenHeight);

            if (variable.Name == "sys.clipboard")
            {
                _clipboardRows.Add(row);
            }

            SystemVariables.Add(row);
        }

        MacroVariables.Clear();
        foreach (var macro in _macros)
        {
            var names = macro.LocalVariableNames();
            if (names.Count == 0)
            {
                continue;
            }

            MacroVariables.Add(new MacroVariableGroup
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

        OnPropertyChanged(nameof(HasMacroVariables));
        RebuildCurrent();
    }

    /// <summary>
    /// Lists the variables the macros in the list actually use, with the scope and the
    /// macros that use them, so their state can be checked before a macro runs. A name a
    /// macro uses but no step defines is called out, which is usually a typo.
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

        foreach (var macro in _macros)
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
                     .OrderBy(entry => Order(entry.Scope))
                     .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new VariableDefinition
            {
                Name = usage.Name,
                Scope = usage.Scope,
                Owner = string.Join(", ", usage.Owners),
                Description = Describe(usage, system, globals),
                Type = Type(usage, system, globals),
                CurrentValue = Value(usage, system, globals, live),
            };

            if (usage.Scope is VariableScope.System && usage.Name == "sys.clipboard")
            {
                _clipboardRows.Add(row);
            }

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
                    case ActionParameterKind.Condition:
                        if (parameter.Condition is not null)
                        {
                            Collect(parameter.Condition);
                        }

                        break;
                    default:
                        var text = parameter.Value.Trim();
                        if (text.Length == 0)
                        {
                            break;
                        }

                        // An expression can read several variables anywhere inside it, so
                        // every name it mentions counts, not just a leading "$name".
                        if (parameter.Kind is ActionParameterKind.Expression)
                        {
                            foreach (var reference in Expression.ReferencedNames(text))
                            {
                                names.Add(reference);
                            }

                            break;
                        }

                        if (text.StartsWith('$'))
                        {
                            var reference = text[1..].Trim();
                            if (reference.Length > 0)
                            {
                                names.Add(reference);
                            }

                            break;
                        }

                        if (known.Contains(text))
                        {
                            names.Add(text);
                            break;
                        }

                        // A parameter that always names a variable can point at a name
                        // nothing defines, which is worth showing while debugging.
                        var declares = step.Definition?.Parameters
                            .FirstOrDefault(candidate => candidate.Name == parameter.Name)?.NamesVariable;
                        if (declares == true)
                        {
                            names.Add(text);
                        }

                        break;
                }
            }
        }
    }

    private static int Order(VariableScope scope) => scope switch
    {
        VariableScope.Global => 0,
        VariableScope.Local => 1,
        VariableScope.System => 2,
        _ => 3,
    };

    private static string Describe(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals) => usage.Scope switch
        {
            VariableScope.System when system.TryGetValue(usage.Name, out var builtIn) => builtIn.LocalDescription,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared) => shared.Description,
            VariableScope.Local => Strings.Get("Variable.LocalDescription"),
            _ => Strings.Get("Variable.UnknownHint"),
        };

    private static string Type(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals)
        => usage.Scope switch
        {
            VariableScope.System when system.TryGetValue(usage.Name, out var builtIn) => builtIn.Type,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared) => shared.Type,
            _ => "text",
        };

    private static string Value(Usage usage,
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

    /// <summary>Fills in the clipboard text, which cannot be read while the rows are built.</summary>
    public void SetClipboard(string text)
    {
        _clipboard = text;
        foreach (var row in _clipboardRows)
        {
            row.CurrentValue = text;
        }
    }

    [RelayCommand]
    private void Refresh() => Rebuild();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddGlobal()
    {
        VariableCatalog.Globals.Add(new VariableDefinition
        {
            Name = NewName.Trim(),
            Scope = VariableScope.Global,
            Type = NewType.Value,
            DefaultValue = NewDefault.Trim(),
            Description = NewDescription.Trim(),
        });

        NewName = string.Empty;
        NewDefault = string.Empty;
        NewDescription = string.Empty;
    }

    [RelayCommand]
    private void RemoveGlobal(VariableDefinition? variable)
    {
        if (variable is not null)
        {
            VariableCatalog.Globals.Remove(variable);
        }
    }
}
