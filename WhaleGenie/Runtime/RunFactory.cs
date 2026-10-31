using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Builds the runner a run needs. Three places start one — the debugger, a trigger and an MCP call —
/// and every one of them used to repeat the same settings by hand: a run that had to carry one more
/// thing had to be changed in three files, and the one that got missed failed in a way that read like
/// the macro's own fault.
/// </summary>
internal static class RunFactory
{
    internal static MacroRunner Create(RunRequest request)
        => new(request.Variables, request.Host, request.Devices, request.Scale, request.Library,
            request.Looks)
        {
            // Where a failure log goes does not depend on who started the run, so it is decided
            // here rather than three times.
            FailureLogFolder = AppPaths.Logs,
            RunName = request.Name,
            ExclusiveInput = request.ExclusiveInput,
            StartSignature = request.StartSignature,
            StartArguments = request.StartArguments,
        };
}

/// <summary>
/// What one run needs. Everything that identifies the run is required, so a caller cannot forget the
/// name it will appear under in the logs; the rest is what a particular kind of caller knows about.
/// </summary>
internal sealed record RunRequest
{
    /// <summary>The variables this run reads and writes. A trigger or an MCP call makes a fresh one.</summary>
    public required VariableStore Variables { get; init; }

    /// <summary>Who to ask when a step fails or wants an answer, and whose step hooks run.</summary>
    public required IRunHost Host { get; init; }

    /// <summary>The machine the steps act on.</summary>
    public required IDeviceLayer Devices { get; init; }

    /// <summary>The macros a call step may name.</summary>
    public required IMacroLibrary Library { get; init; }

    /// <summary>What the run is called in its log and in the debug window.</summary>
    public required string Name { get; init; }

    /// <summary>How much slower or faster than written the macro's own timings are taken.</summary>
    public double Scale { get; init; } = 1;

    /// <summary>Who shows what a trial looks at; the debugger watches, a trigger does not.</summary>
    public IRunLooks? Looks { get; init; }

    /// <summary>Whether the macro took the machine's input for itself, so nothing else may send any.</summary>
    public bool ExclusiveInput { get; init; }

    /// <summary>The declaration of the macro being run when it is one that can be called.</summary>
    public MacroSignature? StartSignature { get; init; }

    /// <summary>What the caller passed in, when the run was started as a call rather than a run.</summary>
    public IReadOnlyList<MacroStartArgument>? StartArguments { get; init; }
}
