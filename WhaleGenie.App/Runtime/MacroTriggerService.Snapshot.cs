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

// The list the service reads the macros from, and the snapshot each pass takes of it.
public sealed partial class MacroTriggerService
{
    /// <summary>The project's macros, in the shape a "run another macro" step calls them by.</summary>
    private IMacroLibrary Library()
        => new ProjectMacroLibrary(Snapshot()
            .Where(macro => macro.Name.Trim().Length > 0)
            .Select(macro => (macro.Name.Trim(), macro.Steps.ToExecutable(),
                macro.Callable.Signature)));

    private IReadOnlyList<MacroItem> Snapshot()
    {
        try
        {
            if (_runtimeSource is { } runtime)
            {
                return runtime().Macros;
            }

            // A copy, because the list is the interface's and it is read from other threads.
            return [.. _macros()];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private TriggerRuntimeSnapshot TriggerSnapshot()
    {
        try
        {
            if (_runtimeSource is { } runtime)
            {
                return runtime() ?? TriggerRuntimeSnapshot.Empty;
            }

            var triggers = _triggerSource() ?? new ProjectTriggers();
            if (_legacyMode)
            {
                foreach (var macro in Snapshot())
                {
                    var entry = triggers.ForMacro(macro.Name).FirstOrDefault();
                    if (entry is null)
                    {
                        entry = TriggerEntry.FromMacro(macro);
                        triggers.Entries.Add(entry);
                    }
                    else
                    {
                        entry.SyncFromMacro(macro);
                    }
                }
            }

            return TriggerRuntimeSnapshot.FromLive(triggers, Snapshot());
        }
        catch (Exception)
        {
            return TriggerRuntimeSnapshot.Empty;
        }
    }

    private MacroItem? MacroFor(TriggerEntry entry)
    {
        return Snapshot().FirstOrDefault(macro =>
            string.Equals(macro.Name.Trim(), entry.MacroName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    internal static void RunGuarded(string stage, Action work)
    {
        try
        {
            work();
        }
        catch (Exception error)
        {
            AppDiagnostics.Record(stage, error);
        }
    }
}
