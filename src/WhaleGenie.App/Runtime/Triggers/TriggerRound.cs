using System;
using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>What the runtime can do to a macro on behalf of a trigger.</summary>
internal interface ITriggerRunner
{
    /// <summary>Starts the macro if it can be started, and answers whether it did.</summary>
    bool TryStart(ArmedTrigger trigger, bool repeating);

    void Stop(string macroName);

    bool IsRunning(string macroName);
}

/// <summary>One watch, asked on its own rhythm whether anything came true.</summary>
internal interface ITriggerPoll
{
    TriggerKind Kind { get; }

    void Poll(TriggerRound round);

    /// <summary>What this watch is waiting for, for the run window, one row per armed trigger.</summary>
    IEnumerable<TriggerWatch> Report(TriggerRound round);
}

/// <summary>A watch answered by the hook rather than by looking: a key going down or coming up.</summary>
internal interface ITriggerInput
{
    void Pressed(TriggerRound round, string key);

    void Released(TriggerRound round, string key);
}

/// <summary>
/// One pass of the watches: the plan being watched, the state that survives between passes, and the
/// clock the pass is running on.
/// </summary>
internal sealed class TriggerRound(TriggerPlan plan, ITriggerRunner runner, TriggerStateStore state,
    DateTime now, DateTime lastPolledAt)
{
    public TriggerPlan Plan { get; } = plan;

    public TriggerStateStore State { get; } = state;

    public DateTime Now { get; } = now;

    /// <summary>When the last pass of the shared clock looked, which is what "checked" means.</summary>
    public DateTime LastPolledAt { get; } = lastPolledAt;

    public void Start(ArmedTrigger trigger, bool repeating) => runner.TryStart(trigger, repeating);

    public bool IsRunning(ArmedTrigger trigger) => runner.IsRunning(trigger.Macro.Name);

    public void Stop(ArmedTrigger trigger) => runner.Stop(trigger.Macro.Name);
}
