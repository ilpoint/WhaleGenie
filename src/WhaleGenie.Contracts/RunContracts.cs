using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One debugger session as the run panel draws it.</summary>
/// <remarks>
/// The five pieces of data a macro author is missing when a macro does not do what they meant sit
/// at the tail, each defaulted so an older caller still builds a whole state: what is holding the
/// input door and at what rate, why the panel switched macros, what each armed trigger is waiting
/// for, what the watched names hold, and every frame the run kept with the one it is showing. A
/// breakpoint's condition has names to start from as well, because remembering one from another
/// window is what a wrong condition is made of.
/// </remarks>
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
    IReadOnlyList<RunStep>? Steps,
    IReadOnlyList<RunLine> Log,
    int LogFrom = 0,
    int InputRate = 0,
    string InputRateOwner = "",
    string SwapNote = "",
    IReadOnlyList<RunTrigger>? Triggers = null,
    IReadOnlyList<RunWatch>? Watches = null,
    IReadOnlyList<RunLook>? Looks = null,
    string ActiveLook = "",
    IReadOnlyList<string>? BreakpointChoices = null);

/// <summary>The last frame a step looked at, already as a data URL for the page.</summary>
[BridgeContract]
public sealed record RunLook(string StepId, string Type, string Looking, string Image);

/// <summary>
/// One armed trigger as the panel lists it: which macro it starts, what it waits for, whether that
/// is there right now, when it was last looked at and what else the machine says about it. Every
/// part is already worded, because the words come from the table rather than from the page.
/// </summary>
/// <param name="Name">The macro the entry starts.</param>
/// <param name="Kind">What it waits for, in the words the macro was written with.</param>
/// <param name="Status">Whether that is there right now, or empty where that is not a question.</param>
/// <param name="Checked">When it was last looked at and when it will be next, already worded.</param>
/// <param name="Detail">Anything else about the waiting, such as the machine telling it instead.</param>
[BridgeContract]
public sealed record RunTrigger(string Name, string Kind, string Status, string Checked, string Detail);

/// <summary>One watched name as it stands right now: what it is called, what it holds and its type.</summary>
[BridgeContract]
public sealed record RunWatch(string Name, string Type, string Value);

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

/// <summary>A batch of new log lines, addressed by the page's cursor.</summary>
[BridgeContract]
public sealed record RunLogBatch(int LogFrom, IReadOnlyList<RunLine> Lines);

/// <summary>One step row: where it is, what it is doing and what it came to.</summary>
/// <remarks>
/// The ending and what the step worked out come last: a step that has not run yet has neither, and
/// an empty field says that rather than a word the panel would have to guess at.
/// </remarks>
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
    string Expression,
    string Outcome = "",
    string Result = "");

/// <summary>One line of the run log.</summary>
[BridgeContract]
public sealed record RunLine(string Time, string Level, string Text);
