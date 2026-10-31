using System;
using System.Collections.Generic;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests.Bench;

/// <summary>
/// The one macro the bench's library offers, so the two call actions have something to call. It
/// declares one argument and one result, and its body writes that result: a call that asks for it
/// comes back with a value rather than a hole.
/// </summary>
internal sealed class BenchLibrary : IMacroLibrary
{
    public static BenchLibrary Instance { get; } = new();

    public IReadOnlyList<string> Names => [BenchFixtures.CalledMacro];

    public IReadOnlyList<ExecutableStep>? Steps(string name)
        => Same(name)
            ? [
                new ExecutableStep
                {
                    Type = "control.setVariable",
                    Parameters =
                    [
                        new ExecutableParameter { Name = "name", Text = BenchFixtures.CalledResult },
                        new ExecutableParameter { Name = "scope", Text = "local" },
                        new ExecutableParameter { Name = "value", Text = "1" },
                    ],
                },
            ]
            : null;

    public MacroSignature? Signature(string name)
        => Same(name)
            ? new MacroSignature(
                [new MacroArgument(BenchFixtures.CalledArgument, string.Empty, false)],
                [new MacroReturn(BenchFixtures.CalledResult)])
            : null;

    private static bool Same(string name)
        => string.Equals(name, BenchFixtures.CalledMacro, StringComparison.OrdinalIgnoreCase);
}
