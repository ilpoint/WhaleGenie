using System;
using System.Text.Json.Nodes;

namespace Viktor.Core.Execution;

/// <summary>What a macro does when a step fails.</summary>
public enum StepErrorAction
{
    /// <summary>Stop the macro at the failing step. The default.</summary>
    Stop,

    /// <summary>Note the failure and carry on with the next step.</summary>
    Continue,

    /// <summary>
    /// Leave the rest of this round of the innermost loop and start the next one. Outside a
    /// loop it behaves like <see cref="Continue"/>.
    /// </summary>
    NextIteration,

    /// <summary>Pause and let the user choose between retrying, skipping and stopping.</summary>
    AskUser,
}

/// <summary>How long a step waits before each of its retries.</summary>
public enum RetryBackoff
{
    /// <summary>The same pause before every attempt. The default.</summary>
    Fixed,

    /// <summary>Twice the pause before each attempt, so a step that keeps failing backs off.</summary>
    Doubling,

    /// <summary>A pause somewhere around the one asked for, so retries do not march in step.</summary>
    Jitter,
}

/// <summary>
/// The settings every step carries besides its action: a note, whether it is skipped,
/// how long it may take, how often it is retried, and what happens when it fails. Only the
/// settings that differ from these defaults are written to the macro file.
/// </summary>
public class StepMeta
{
    /// <summary>The settings a step has when nothing has been changed.</summary>
    public static StepMeta Empty { get; } = new();

    /// <summary>
    /// The longest a retry pause may grow to. A doubling pause that is left to run would
    /// otherwise put a macro to sleep for hours without ever saying so.
    /// </summary>
    public const int MostRetryDelayMs = 30_000;

    /// <summary>Note the user wrote about this step.</summary>
    public string Comment { get; init; } = string.Empty;

    /// <summary>False when the macro skips the step.</summary>
    public bool IsEnabled { get; init; } = true;

    /// <summary>How long the step may take before it counts as failed. 0 means no limit.</summary>
    public int TimeoutMs { get; init; }

    /// <summary>How many extra attempts a failing step gets.</summary>
    public int RetryCount { get; init; }

    /// <summary>Pause between two attempts.</summary>
    public int RetryDelayMs { get; init; } = 500;

    /// <summary>How that pause grows from one attempt to the next.</summary>
    public RetryBackoff RetryBackoff { get; init; } = RetryBackoff.Fixed;

    /// <summary>Pause before the step runs.</summary>
    public int DelayBeforeMs { get; init; }

    /// <summary>Pause after the step has run.</summary>
    public int DelayAfterMs { get; init; }

    /// <summary>What the macro does when the step fails.</summary>
    public StepErrorAction OnError { get; init; } = StepErrorAction.Stop;

    /// <summary>True when nothing has been changed, so no <c>meta</c> node needs writing.</summary>
    public bool IsEmpty => Comment.Length == 0
        && IsEnabled
        && TimeoutMs <= 0
        && RetryCount <= 0
        && DelayBeforeMs <= 0
        && DelayAfterMs <= 0
        && OnError is StepErrorAction.Stop;

    /// <summary>The same settings with the enabled flag changed, used by the skip toggle.</summary>
    public StepMeta WithEnabled(bool enabled) => new()
    {
        Comment = Comment,
        IsEnabled = enabled,
        TimeoutMs = TimeoutMs,
        RetryCount = RetryCount,
        RetryDelayMs = RetryDelayMs,
        RetryBackoff = RetryBackoff,
        DelayBeforeMs = DelayBeforeMs,
        DelayAfterMs = DelayAfterMs,
        OnError = OnError,
    };

    /// <summary>
    /// How long to wait before the given retry, counting the first retry as 1. The wait is read
    /// from the settings alone, so what a macro will do between two attempts can be shown to the
    /// user before the macro is ever run.
    /// </summary>
    public int RetryDelayFor(int attempt, Random? random = null)
    {
        var number = Math.Max(1, attempt);
        var delay = RetryBackoff switch
        {
            RetryBackoff.Doubling => Math.Min(MostRetryDelayMs, (long)RetryDelayMs << Math.Min(number - 1, 20)),
            RetryBackoff.Jitter => (long)Math.Round(RetryDelayMs * (0.5 + (random ?? Random.Shared).NextDouble())),
            _ => RetryDelayMs,
        };

        return (int)Math.Clamp(delay, 0, MostRetryDelayMs);
    }

    /// <summary>Writes only the settings that differ from the defaults.</summary>
    public JsonObject ToJson()
    {
        var node = new JsonObject();
        if (Comment.Length > 0)
        {
            node["comment"] = Comment;
        }

        if (!IsEnabled)
        {
            node["enabled"] = false;
        }

        if (TimeoutMs > 0)
        {
            node["timeoutMs"] = TimeoutMs;
        }

        if (RetryCount > 0)
        {
            node["retry"] = RetryCount;
            node["retryDelayMs"] = RetryDelayMs;
            if (RetryBackoff is not RetryBackoff.Fixed)
            {
                node["retryBackoff"] = Name(RetryBackoff);
            }
        }

        if (DelayBeforeMs > 0)
        {
            node["delayBeforeMs"] = DelayBeforeMs;
        }

        if (DelayAfterMs > 0)
        {
            node["delayAfterMs"] = DelayAfterMs;
        }

        if (OnError is not StepErrorAction.Stop)
        {
            node["onError"] = Name(OnError);
        }

        return node;
    }

    /// <summary>Rebuilds the settings from a step's <c>meta</c> node.</summary>
    public static StepMeta FromJson(JsonObject? node)
    {
        if (node is null)
        {
            return Empty;
        }

        var retryDelay = Number(node, "retryDelayMs");
        return new StepMeta
        {
            Comment = Text(node, "comment"),
            IsEnabled = node["enabled"]?.GetValue<bool>() ?? true,
            TimeoutMs = Number(node, "timeoutMs"),
            RetryCount = Number(node, "retry"),
            RetryDelayMs = retryDelay > 0 ? retryDelay : 500,
            RetryBackoff = Backoff(Text(node, "retryBackoff")),
            DelayBeforeMs = Number(node, "delayBeforeMs"),
            DelayAfterMs = Number(node, "delayAfterMs"),
            OnError = Action(Text(node, "onError")),
        };
    }

    /// <summary>The word written to the macro file for a failure rule.</summary>
    public static string Name(StepErrorAction action) => action switch
    {
        StepErrorAction.Continue => "continue",
        StepErrorAction.NextIteration => "nextIteration",
        StepErrorAction.AskUser => "ask",
        _ => "stop",
    };

    /// <summary>Reads a failure rule back, treating anything unknown as "stop".</summary>
    public static StepErrorAction Action(string word) => word.ToLowerInvariant() switch
    {
        "continue" => StepErrorAction.Continue,
        "nextiteration" => StepErrorAction.NextIteration,
        "ask" => StepErrorAction.AskUser,
        _ => StepErrorAction.Stop,
    };

    /// <summary>The word written to the macro file for a backoff.</summary>
    public static string Name(RetryBackoff backoff) => backoff switch
    {
        RetryBackoff.Doubling => "doubling",
        RetryBackoff.Jitter => "jitter",
        _ => "fixed",
    };

    /// <summary>Reads a backoff back; a file written before this setting existed means "fixed".</summary>
    public static RetryBackoff Backoff(string word) => word.ToLowerInvariant() switch
    {
        "doubling" => RetryBackoff.Doubling,
        "jitter" => RetryBackoff.Jitter,
        _ => RetryBackoff.Fixed,
    };

    private static string Text(JsonObject node, string name)
        => node[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static int Number(JsonObject node, string name)
    {
        if (node[name] is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<int>(out var whole))
        {
            return whole;
        }

        return value.TryGetValue<double>(out var real) ? (int)real : 0;
    }
}
