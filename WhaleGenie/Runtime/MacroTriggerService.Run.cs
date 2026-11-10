using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Runtime;

// Running a macro the trigger chose, and the guard around each stage of it.
public sealed partial class MacroTriggerService
{

    // ---------------------------------------------------------------- running

    /// <summary>Runs one macro once for a floating button or menu selection.</summary>
    public void RunOnce(MacroItem macro)
    {
        if (macro.IsEnabled)
        {
            Start(macro, repeating: false);
        }
    }

    private void StartFrom(TriggerEntry entry, MacroItem macro, bool repeating)
    {
        lock (_gate)
        {
            if (entry.TriggerOnce && _spent.Contains(entry))
            {
                return;
            }

            if (entry.TriggerOnce)
            {
                _spent.Add(entry);
            }
        }

        Start(macro, repeating);
    }

    private void Start(MacroItem macro, bool repeating)
    {
        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (!_enabled || _disposed || macro.Steps.Count == 0 || _running.ContainsKey(macro))
            {
                return;
            }

            cancellation = new CancellationTokenSource();
            _running[macro] = cancellation;
        }

        StatusChanged?.Invoke();
        _ = Task.Run(() => RunAsync(macro, repeating, cancellation));
    }

    private async Task RunAsync(MacroItem macro, bool repeating, CancellationTokenSource cancellation)
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
                        new SystemVariableReader(_devices), SharedVariables.Current),
                    Host = host,
                    Devices = _devices,
                    Library = library,
                    Name = macro.Name,
                    Scale = macro.DelayScale,
                    InputRate = macro.InputRate,
                    Looks = capture,
                    StartSignature = macro.Callable.Signature,
                });
                var result = await runner.RunAsync(steps, token);
                last = result;
                if (result.Status != RunStatus.Completed || !repeating || StoppingAfterPass(macro))
                {
                    break;
                }

                await Task.Delay(RepeatGapMs, token);
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
                _running.Remove(macro);
                _colourHeld.Remove(macro);
                _colourStopping.Remove(macro);
            }

            cancellation.Dispose();
            StatusChanged?.Invoke();
        }
    }
}
