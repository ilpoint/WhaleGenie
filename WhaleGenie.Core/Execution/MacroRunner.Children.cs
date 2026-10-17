using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The run a background call started, what its handle says about it, and the host it reports
/// through: the parts of a call that outlive the step that started it.
/// </summary>
public sealed partial class MacroRunner
{
    /// <summary>One background call, kept until the macro waits for it.</summary>
    private sealed class ChildRun
    {
        public required string Handle { get; init; }

        public required string Macro { get; init; }

        public required MacroSignature Signature { get; init; }

        public required VariableStore Store { get; init; }

        public required CancellationTokenSource Stop { get; init; }

        public required List<(string Result, string Name)> Wanted { get; init; }

        public required Task<RunResult> Task { get; init; }

        /// <summary>The caller's local variable that holds this run's handle.</summary>
        public required string HandleName { get; init; }
    }

    /// <summary>What a background run's handle says about the run right now.</summary>
    private static class RunHandleStatus
    {
        public const string Running = "running";
        public const string Done = "done";
        public const string Failed = "failed";
        public const string Stopped = "stopped";
    }

    /// <summary>
    /// What a background run reports through. Its lines land in the caller's log so a run window
    /// still shows what happened; its steps do not stop at the caller's breakpoints, which belong
    /// to the steps the window is showing.
    /// </summary>
    private sealed class ChildRunHost(IRunHost parent) : IRunHost
    {
        public void Log(LogEntry entry) => parent.Log(entry);

        public string Text(LogEntry entry) => parent.Text(entry);

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromResult(true);

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => parent.Ask(step, reason, detail, comment, token);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => parent.AskUser(question, token);
    }

}
