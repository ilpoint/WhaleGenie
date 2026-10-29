using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Mcp;

/// <summary>
/// Running a macro on request, which is what lets a client check its own work instead of handing it
/// over and hoping. Two things keep it from being a way around the person: the switch in the flyout
/// has to be on, and only one run happens at a time — a second call is refused rather than queued,
/// because two runs of two macros on one machine is not something a client can reason about.
/// </summary>
/// <remarks>
/// The macro can be the one in the project or the one in a draft. Running the draft is the point: a
/// client can try a change, read what the steps answered, change it again, and only then apply —
/// which is the loop this service exists for.
/// </remarks>
[McpServerToolType]
public sealed class McpRunTools
{
    private static readonly object Gate = new();

    private static CancellationTokenSource? _running;
    private static string _runningId = string.Empty;
    private static string _runningMacro = string.Empty;
    private static DateTimeOffset _started;

    private readonly MainViewModel _ui;
    private readonly McpDraftStore _drafts;
    private readonly Func<IDeviceLayer> _devices;

    public McpRunTools(MainViewModel ui, McpDraftStore drafts)
        : this(ui, drafts, () => SharedDevices.Current)
    {
    }

    /// <summary>A bench or a check hands its own device layer in here.</summary>
    internal McpRunTools(MainViewModel ui, McpDraftStore drafts, Func<IDeviceLayer> devices)
    {
        _ui = ui;
        _drafts = drafts;
        _devices = devices;
    }

    [McpServerTool(Name = "whalegenie_run_macro", Destructive = true,
            Title = "Run a macro"),
     Description("Runs one macro and answers with how it ended and what every step worked out. Needs the person to have turned on \"Allow the AI to run macros\" in the WhaleGenie MCP flyout, which is a different switch from \"Allow direct control\"; without it the call is refused with RunNotAllowed. Pass a draftId to run the draft's version, which is how a change is tried before it is applied. A run already going makes this one refuse with RunBusy. The answer carries the run's id, which stop_macro takes and run_status reports.")]
    public Task<CallToolResult> RunMacro(
        [Description("The macro name, as whalegenie_list_macros shows it.")] string name,
        [Description("A draft id to run that draft's version instead of the project's.")] string? draftId = null,
        [Description("How long the whole run may take, in milliseconds. 120000 by default.")] int timeoutMs = 120_000)
        => McpErrors.RunAsync(async () =>
        {
            if (!McpControl.AllowsRun)
            {
                McpAudit.Note("run_macro", "refused", McpErrors.RunNotAllowed);
                return McpErrors.Fail(McpErrors.RunNotAllowed,
                    "Running a macro is not allowed right now, so nothing was run.",
                    "Ask the person to turn on \"Allow the AI to run macros\" in the WhaleGenie MCP flyout. It is a separate switch from \"Allow direct control\", and a running server reads it before every call, so nothing has to be restarted.");
            }

            var wanted = (name ?? string.Empty).Trim();
            var sources = await Dispatcher.UIThread.InvokeAsync(() => Sources(draftId));
            if (sources.Refusal is { } refusal)
            {
                return refusal;
            }

            var macro = sources.Macros.FirstOrDefault(candidate =>
                string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            if (macro is null)
            {
                return McpErrors.Fail(McpErrors.NoSuchMacro, $"There is no macro named \"{wanted}\".",
                    draftId is { Length: > 0 }
                        ? "Check the draft with whalegenie_list_macros; a draft holds only what was written into it."
                        : "Check the project with whalegenie_list_macros, or pass a draftId to run a draft.",
                    retryable: true, details: new { name = wanted });
            }

            // Everything the run needs is copied off the view model here, on the interface thread:
            // the run itself then works with plain objects on a thread of its own.
            var steps = macro.Steps.ToExecutable();
            var library = new ProjectMacroLibrary(CallableMacros.Entries(sources.Macros));

            var signature = macro.Callable.Signature;
            var exclusive = macro.ExclusiveInput;
            var scale = macro.DelayScale;
            var runName = macro.Name.Trim();

            CancellationTokenSource token;
            lock (Gate)
            {
                if (_running is not null)
                {
                    return McpErrors.Fail(McpErrors.RunBusy, $"\"{_runningMacro}\" is already running.",
                        "Stop it with whalegenie_stop_macro, or wait for it to finish and call again.",
                        retryable: true, details: new { running = _runningMacro, runId = _runningId });
                }

                token = new CancellationTokenSource();
                _running = token;
                _runningMacro = runName;
                _started = DateTimeOffset.Now;
            }

            var capture = RunLogStore.Begin(runName, "mcp");
            lock (Gate)
            {
                _runningId = capture.Id;
            }

            try
            {
                var devices = _devices();
                var host = new AskRunHost(null, null, capture.Add);
                var runner = RunFactory.Create(new RunRequest
                {
                    Variables = MacroVariables.Seed(
                        new SystemVariableReader(devices), SharedVariables.Current),
                    Host = host,
                    Devices = devices,
                    Library = library,
                    Name = runName,
                    Scale = scale,
                    Looks = capture,
                    ExclusiveInput = exclusive,
                    StartSignature = signature,
                });

                var budget = Math.Clamp(timeoutMs, 1000, 3_600_000);
                using var deadline = new CancellationTokenSource(budget);
                using var both = CancellationTokenSource.CreateLinkedTokenSource(token.Token, deadline.Token);

                RunResult? result = null;
                var timedOut = false;
                try
                {
                    result = await Task.Run(() => runner.RunAsync(steps, both.Token), both.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Stopping and running out of time are both normal endings for a run.
                    timedOut = deadline.IsCancellationRequested;
                }

                var ended = result is not null
                    ? result.Status.ToString()
                    : timedOut ? "Timeout" : "Stopped";
                capture.Finish(result, ended);
                McpAudit.Note("run_macro", $"{runName} {ended}");

                return McpErrors.Ok(new
                {
                    runId = capture.Id,
                    macro = runName,
                    status = ended,
                    key = result?.Key ?? string.Empty,
                    detail = result?.Detail ?? string.Empty,
                    steps = result?.Steps ?? 0,
                    timedOut,
                    outcomes = RunLogStore.Find(capture.Id)?.Outcomes ?? [],
                });
            }
            finally
            {
                lock (Gate)
                {
                    _running = null;
                    _runningId = string.Empty;
                    _runningMacro = string.Empty;
                }

                token.Dispose();
            }
        });

    [McpServerTool(Name = "whalegenie_stop_macro", Destructive = true,
            Title = "Stop a run"),
     Description("Asks the run that is going to stop. A step already under way finishes or gives up on its own timeout; everything after it is skipped, and the run ends as Stopped.")]
    public Task<CallToolResult> StopMacro(
        [Description("The run id whalegenie_run_macro answered with; empty stops whatever is running.")] string? runId = null)
    {
        lock (Gate)
        {
            if (_running is null)
            {
                return Task.FromResult(McpErrors.Fail(McpErrors.NoSuchRun, "Nothing is running.",
                    "Call whalegenie_list_runs to see what the last runs were.", retryable: true));
            }

            var asked = (runId ?? string.Empty).Trim();
            if (asked.Length > 0
                && !string.Equals(asked, _runningId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(McpErrors.Fail(McpErrors.NoSuchRun,
                    $"The run going is \"{_runningId}\", not \"{asked}\".",
                    "Call whalegenie_run_status to see the run that is going.", retryable: true,
                    details: new { runId = _runningId }));
            }

            _running.Cancel();
            McpAudit.Note("stop_macro", _runningId);
            return Task.FromResult(McpErrors.Ok(new { stopped = _runningId }));
        }
    }

    [McpServerTool(Name = "whalegenie_run_status", ReadOnly = true,
            Title = "What is running"),
     Description("Says whether a run is going right now and what it is, so a client can tell a stopped run from one that never started.")]
    public Task<CallToolResult> RunStatus()
    {
        lock (Gate)
        {
            return Task.FromResult(McpErrors.Ok(_running is null
                ? new
                {
                    running = false,
                    runId = string.Empty,
                    macro = string.Empty,
                    started = (DateTimeOffset?)null,
                }
                : new
                {
                    running = true,
                    runId = _runningId,
                    macro = _runningMacro,
                    started = (DateTimeOffset?)_started,
                }));
        }
    }

    /// <summary>The macros a run can reach, and a refusal when the draft it names is not there.</summary>
    private (IReadOnlyList<MacroItem> Macros, CallToolResult? Refusal) Sources(string? draftId)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            return (_ui.Macros.ToList(), null);
        }

        try
        {
            return (_drafts.Get(draftId).MacroSnapshot(), null);
        }
        catch (McpFailure failure)
        {
            return ([], failure.ToResult());
        }
    }
}
