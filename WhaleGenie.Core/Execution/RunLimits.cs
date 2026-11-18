namespace WhaleGenie.Core.Execution;

/// <summary>
/// The ceilings a step runs into, written once so every search and every table read is bounded the
/// same way. They are about memory rather than about meaning: a step that asked for everything gets
/// as much as fits, not as much as exists.
/// </summary>
internal static class RunLimits
{
    /// <summary>The most hits one search collects, so a flat colour cannot fill memory.</summary>
    public const int Matches = 200;

    /// <summary>The most rows one step reads, so a grid that keeps growing cannot fill memory.</summary>
    public const int Rows = 10_000;
}
