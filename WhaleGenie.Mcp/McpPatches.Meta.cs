using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;


namespace WhaleGenie.Mcp;

// The settings around a step: its note, its failure rule and its timing.
internal static partial class McpPatches
{
    /// <summary>
    /// Changes what a step says about itself: its note, whether it is skipped, its timeout, its
    /// retries, the pauses around it and what a failure does. These are the settings a macro author
    /// adjusts on almost every step, and they had no way in short of sending the whole macro.
    /// </summary>
    private static Refusal? Meta(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var step = Located(macro, operation);
        if (step is null)
        {
            return NoStep(operation);
        }

        var before = step.Meta;
        var next = new StepMeta
        {
            Comment = Said(operation, "comment") ?? before.Comment,
            IsEnabled = Flag(operation, "enabled") ?? before.IsEnabled,
            TimeoutMs = Number(operation, "timeoutMs") ?? before.TimeoutMs,
            RetryCount = Number(operation, "retryCount") ?? before.RetryCount,
            RetryDelayMs = Number(operation, "retryDelayMs") ?? before.RetryDelayMs,
            RetryBackoff = Picked<RetryBackoff>(operation, "retryBackoff") ?? before.RetryBackoff,
            DelayBeforeMs = Number(operation, "delayBeforeMs") ?? before.DelayBeforeMs,
            DelayAfterMs = Number(operation, "delayAfterMs") ?? before.DelayAfterMs,
            OnError = Picked<StepErrorAction>(operation, "onError") ?? before.OnError,
        };

        if (next.TimeoutMs is < 0 or > StepMeta.LongestPauseMs
            || next.DelayBeforeMs is < 0 or > StepMeta.LongestPauseMs
            || next.DelayAfterMs is < 0 or > StepMeta.LongestPauseMs)
        {
            return Refused(McpErrors.BadArgument,
                "A pause or timeout is outside what a step may ask for.",
                $"A length of time is between 0 and {StepMeta.LongestPauseMs} milliseconds.",
                true, new { step = step.Type, longest = StepMeta.LongestPauseMs });
        }

        step.Meta = next;
        touches.Add(new Touch("meta", step));
        return null;
    }
}
