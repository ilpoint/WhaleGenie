using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The other macros a run can hand work to. The editor brings its own, so a macro that calls
/// another one is only ever able to reach the macros in the same project.
/// </summary>
public interface IMacroLibrary
{
    /// <summary>The names of the macros that can be called, in the order they are listed.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>
    /// The steps of the macro with this name, ready to run, or null when there is no such
    /// macro. An empty name means "no macro was chosen" and is never a match.
    /// </summary>
    IReadOnlyList<ExecutableStep>? Steps(string name);
}

/// <summary>A library with nothing in it, for a run that has no project behind it.</summary>
public sealed class EmptyMacroLibrary : IMacroLibrary
{
    public static EmptyMacroLibrary Instance { get; } = new();

    public IReadOnlyList<string> Names => Array.Empty<string>();

    public IReadOnlyList<ExecutableStep>? Steps(string name) => null;
}
