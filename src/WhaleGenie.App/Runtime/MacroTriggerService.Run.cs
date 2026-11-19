using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

// Running a macro the trigger chose, and the guard around each stage of it.
public sealed partial class MacroTriggerService
{
    /// <summary>
    /// Starts one armed trigger. A "run once" entry is written down as spent only after the run has
    /// really begun: a start refused because the macro is empty, switched off or already running
    /// must not use the entry up.
    /// </summary>
    public bool TryStart(ArmedTrigger trigger, bool repeating)
    {
        var once = TriggerKinds.RunsOnce(trigger.Spec);
        if (once && _hub.State.IsSpent(trigger.Id))
        {
            return false;
        }

        if (!Start(trigger.Macro, repeating))
        {
            return false;
        }

        if (once)
        {
            _hub.State.MarkSpent(trigger.Id);
        }

        return true;
    }

    private bool Start(MacroRef macro, bool repeating)
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (!Listening || macro.Steps.Count == 0 || _running.ContainsKey(macro.Name))
            {
                return false;
            }

            cancellation = new CancellationTokenSource();
            _running[macro.Name] = new RunSlot(macro, cancellation);
        }

        StatusChanged?.Invoke();
        _ = Task.Run(() => RunAsync(macro, repeating, cancellation));
        return true;
    }

    private async Task RunAsync(MacroRef macro, bool repeating, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var capture = RunLogStore.Begin(macro.Name, "trigger");
        RunResult? last = null;
        try
        {
            var steps = macro.Steps.ToExecutable();
            var library = Library();

            // No debugger is behind a run a trigger started, but a step whose failure rule is
            // "ask me" still has to reach whoever can put the question on screen. The same host
            // also keeps the run's lines for the MCP debug tools.
            var host = new AskRunHost(Ask, AskUser, capture.Add, ConfirmIrreversible);

            while (!token.IsCancellationRequested)
            {
                var runner = RunFactory.Create(new RunRequest
                {
                    Variables = MacroVariables.Seed(
                        new SystemVariableReader(_devices), _variables),
                    Host = host,
                    Devices = _devices,
                    Library = library,
                    Name = macro.Name,
                    Scale = macro.DelayScale,
                    InputRate = macro.InputRate,
                    Looks = capture,
                    StartSignature = macro.Signature,
                });
                var result = await runner.RunAsync(steps, token);
                last = result;
                if (result.Status != RunStatus.Completed || !repeating
                    || _hub.Stopping(macro.Name))
                {
                    break;
                }

                await Task.Delay(TriggerPace.RepeatGapMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping is the normal way for one of these to end.
        }
        catch (Exception)
        {
            // A macro that goes wrong must not take the application down with it.
        }
        finally
        {
            capture.Finish(last, token.IsCancellationRequested
                ? RunStatus.Stopped.ToString()
                : last?.Status.ToString() ?? RunStatus.Failed.ToString());

            lock (_gate)
            {
                _running.Remove(macro.Name);
            }

            _hub.RunEnded(macro.Name);
            cancellation.Dispose();
            StatusChanged?.Invoke();
        }
    }

    /// <summary>The plan's macros, in the shape a "run another macro" step calls them by.</summary>
    private IMacroLibrary Library()
        => new ProjectMacroLibrary(_hub.Plan.Macros
            .Where(macro => macro.Name.Length > 0)
            .Select(macro => (macro.Name, macro.Steps.ToExecutable(), macro.Signature)));
}
