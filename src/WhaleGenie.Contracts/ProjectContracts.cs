using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One macro as the list draws it. The view membership is decided by the program.</summary>
[BridgeContract]
public sealed record MacroEntry(
    string Name,
    string Group,
    string Description,
    bool Enabled,
    bool Running,
    string TriggerSummary,
    string TriggerActions,
    string TriggerPreview,
    IReadOnlyList<string> Views);

/// <summary>The whole project state the list draws from, unrelated to which page is open.</summary>
[BridgeContract]
public sealed record ProjectState(
    string? Path,
    string? Name,
    bool Untitled,
    bool Dirty,
    int Revision,
    bool SystemEnabled,
    bool AutoSave,
    bool Saving,
    string? SaveError,
    bool EnabledFirst,
    IReadOnlyList<string> Views,
    IReadOnlyList<string> Groups,
    IReadOnlyList<MacroEntry> Macros);
