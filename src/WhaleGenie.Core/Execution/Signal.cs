namespace WhaleGenie.Core.Execution;

/// <summary>What a step tells the runner to do next.</summary>
internal enum Signal
{
    Normal,
    Break,
    Continue,
    Return,
    Stop,
    Failed,
}
