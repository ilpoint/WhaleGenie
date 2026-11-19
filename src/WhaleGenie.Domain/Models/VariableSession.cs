using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Models;

/// <summary>
/// The variables a project shares with every run: the declarations the Variable Center keeps, and
/// the values each of them holds now. One of these is made when the program starts and lives as
/// long as it does, so a trigger, the run window, a trial beside a dialog and the MCP tools all
/// write the same table, and a value one macro writes is what the next one reads.
/// </summary>
/// <remarks>
/// It is an object rather than a static because two sessions have nothing to say to each other: a
/// check that opens a project of its own, a second copy of the program, a window built over another
/// project. What used to be one static table was shared state that only happened to have one owner,
/// and every check that touched it had to put it back for the next one.
/// </remarks>
public sealed class VariableSession
{
    private readonly ObservableCollection<VariableDefinition> _globals = [];

    public VariableSession()
    {
        Globals = new ReadOnlyObservableCollection<VariableDefinition>(_globals);

        // A declared value is read before there is a run to read it in, so the editor asks this
        // table instead: the shared values as they stand, with the built-in ones seeded at the
        // values they are declared to start at. Read through it; a write would be a shared write.
        Design = new VariableStore(Values);
        foreach (var variable in VariableCatalog.SystemVariables.Where(variable => variable.Seeded))
        {
            Design.System.Set(variable.Name,
                VariableValues.Read(variable.Type, variable.Default, Design));
        }
    }

    /// <summary>The table every run of this project reads and writes.</summary>
    public VariableBag Values { get; } = new();

    /// <summary>The shared declarations. Read-only from outside: a declaration is taken in by adopting it.</summary>
    public ReadOnlyObservableCollection<VariableDefinition> Globals { get; }

    /// <summary>Raised after a declaration is added, replaced or taken away.</summary>
    public event Action? GlobalsChanged;

    /// <summary>The table the editor reads a declared value through when nothing is running.</summary>
    public VariableStore Design { get; }

    /// <summary>
    /// Takes a declaration into this session. Its starting value is read against the session's own
    /// values — that is what the preview means — so the table is handed to it here rather than by
    /// every window that happens to build one.
    /// </summary>
    public void Adopt(VariableDefinition definition)
    {
        definition.Resolver = Design;
        _globals.Add(definition);
    }

    /// <summary>Takes every declaration away, which is what opening another project means.</summary>
    public void ClearGlobals() => _globals.Clear();

    /// <summary>The declaration this project keeps under a name, or null when it keeps none.</summary>
    public VariableDefinition? FindGlobal(string name)
        => _globals.FirstOrDefault(variable =>
            string.Equals(variable.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when the project declares a shared variable by this name.</summary>
    public bool IsGlobal(string name) => FindGlobal(name) is not null;

    /// <summary>Says the declarations themselves moved, so the windows reading them redraw.</summary>
    public void NotifyGlobalsChanged() => GlobalsChanged?.Invoke();

    /// <summary>
    /// Another table with the same starting point: the shared values as they stand, the built-in
    /// ones seeded, and nothing else. A window that has to write as it reads — checking one row's
    /// starting value before reading the next one's — builds this so what it works out on the way
    /// is not a value any macro will see.
    /// </summary>
    public VariableStore Context()
    {
        var store = new VariableStore();
        foreach (var (name, value) in Values.Values)
        {
            store.Global.Set(name, value);
        }

        foreach (var variable in VariableCatalog.SystemVariables.Where(variable => variable.Seeded))
        {
            store.System.Set(variable.Name,
                VariableValues.Read(variable.Type, variable.Default, store));
        }

        return store;
    }
}
