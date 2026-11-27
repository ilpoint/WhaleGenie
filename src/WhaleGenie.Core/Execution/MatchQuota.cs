using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// How many places a walk that counts them where it finds them has to collect: every one it can hold
/// when the step keeps the whole list or counts from a corner the walk does not start in, and only as
/// far as the place it named otherwise. The decision is kept here rather than beside the walk because
/// it is about what the step asked for, not about what the machine did.
/// </summary>
internal static class MatchQuota
{
    public static int Of(bool every, MatchOrder kind, int index)
        => every || HitOrder.CountsFromTheFarSide(kind) || kind is MatchOrder.Near or MatchOrder.Random
            ? RunLimits.Matches
            : index;
}
