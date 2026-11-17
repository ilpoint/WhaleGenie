using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The trigger service and the plan it reads, together. The window used to build both in its own
/// opened handler and drop them again when it closed; a check that wanted the triggers had to build
/// them the same way, and nothing outside the window could say whether they were running.
/// </summary>
/// <remarks>
/// What stays in the window is what only a window can do: the two dialogs a failed step asks
/// through, and the menu a trigger entry opens. Those are hooked on once the window is up, because
/// until then there is nothing to ask and nowhere to put a menu.
/// </remarks>
internal sealed class TriggerRuntime : IDisposable
{
    public TriggerRuntime(IDeviceLayer devices, VariableSession variables)
        => Triggers = new MacroTriggerService(devices, variables);

    /// <summary>The service that watches for the triggers and starts the runs they ask for.</summary>
    public MacroTriggerService Triggers { get; }

    /// <summary>
    /// Hands over the project as it stands now. A trigger run reads the plan rather than the
    /// project itself, so it can only see what was published before it started.
    /// </summary>
    /// <param name="menuLauncher">
    /// The key that opens the project menu, read by the caller: publishing must not touch the disk
    /// on the interface thread.
    /// </param>
    public void Publish(ProjectTriggers triggers, IReadOnlyList<MacroItem> macros, string menuLauncher)
        => Triggers.Publish(TriggerPlan.From(triggers, macros, menuLauncher));

    public void Dispose() => Triggers.Dispose();
}
