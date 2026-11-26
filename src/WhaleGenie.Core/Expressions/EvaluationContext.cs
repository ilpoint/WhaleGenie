using System;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// What an expression is worked out against besides its own shape and the names it reads: the clock
/// that answers <c>now()</c> and the dice that answer <c>random()</c>. Handing them over rather than
/// reading the machine is what makes the same formula answer the same twice, which is what a check,
/// an editor preview and a mutation run each need.
/// </summary>
public sealed class EvaluationContext
{
    /// <summary>The clock and the dice of the machine this program is running on.</summary>
    public static EvaluationContext System { get; } = new(TimeProvider.System, Random.Shared);

    public EvaluationContext(TimeProvider clock, Random random)
    {
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Random = random ?? throw new ArgumentNullException(nameof(random));
    }

    /// <summary>The clock a date function reads when nothing was given to it.</summary>
    public TimeProvider Clock { get; }

    /// <summary>The dice a random function rolls.</summary>
    public Random Random { get; }

    /// <summary>The moment on this machine, with the offset its own wall time is written in.</summary>
    public DateTimeOffset Now => Clock.GetLocalNow();
}
