using System;
using System.Collections.Generic;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Execution;

/// <summary>
/// The macros of one project, in the shape the engine calls them by. Building it from ready-made
/// step trees keeps the engine free of the editor's own step type, and lets the editor hand over
/// the macro being edited exactly as it stands rather than as it was last saved.
/// </summary>
public sealed class ProjectMacroLibrary : IMacroLibrary
{
    private readonly Dictionary<string, IReadOnlyList<ExecutableStep>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> _names = [];

    public ProjectMacroLibrary(
        IEnumerable<(string Name, IReadOnlyList<ExecutableStep> Steps)> macros)
    {
        foreach (var (name, steps) in macros)
        {
            if (name.Length == 0 || !_byName.TryAdd(name, steps))
            {
                continue;
            }

            _names.Add(name);
        }
    }

    public IReadOnlyList<string> Names => _names;

    public IReadOnlyList<ExecutableStep>? Steps(string name)
        => name.Length > 0 && _byName.TryGetValue(name, out var steps) ? steps : null;
}
