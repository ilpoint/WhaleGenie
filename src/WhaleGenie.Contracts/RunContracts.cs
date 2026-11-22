using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One debugger session as the run panel draws it.</summary>
[BridgeContract]
public sealed record RunState(
    string Loaded,
    string Status,
    string StatusKind,
    bool Busy,
    bool Paused,
    bool CanRun,
    bool CanStep,
    bool CanStop,
    string Question,
    bool WantsTypedAnswer,
    string AnswerText,
    bool IsAskingUser,
    bool IsAsking,
    IReadOnlyList<RunArgument> Arguments,
    RunLook? Look,
    IReadOnlyList<RunGlobal> Globals,
    IReadOnlyList<RunLocal> Locals,
    IReadOnlyList<RunStep> Steps,
    IReadOnlyList<RunLine> Log,
    int LogFrom = 0);

/// <summary>The last frame a step looked at, already as a data URL for the page.</summary>
[BridgeContract]
public sealed record RunLook(string StepId, string Type, string Looking, string Image);

/// <summary>One argument the macro asks for before it starts.</summary>
[BridgeContract]
public sealed record RunArgument(
    string Name, string Label, string Hint, bool Required, string Value, bool Given, bool Missing);

/// <summary>One global variable as the run panel shows it.</summary>
[BridgeContract]
public sealed record RunGlobal(string Name, string Type, string Value, string DefaultValue);

/// <summary>One local variable, named with the macro that owns it.</summary>
[BridgeContract]
public sealed record RunLocal(string Name, string Macro, string Type);

/// <summary>One step row: where it is, what it is doing and what it came to.</summary>
[BridgeContract]
public sealed record RunStep(
    string Id,
    string Label,
    string Detail,
    int Depth,
    bool Shown,
    bool Container,
    bool Current,
    bool Breakpoint,
    bool CanBreak,
    string Time,
    string Failure,
    string Condition,
    int HitTimes,
    string Expression);

/// <summary>One line of the run log.</summary>
[BridgeContract]
public sealed record RunLine(string Time, string Level, string Text);
