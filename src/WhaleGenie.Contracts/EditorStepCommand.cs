using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>
/// One single-step edit. Kind is add/meta/parameter/rowAdd/rowRemove/rowCell; the table kinds
/// address a parameter by name and a row by index.
/// </summary>
[BridgeContract]
[BridgeRequest]
public sealed record EditorStepCommand(
    string Kind,
    string? StepId = null,
    string? Type = null,
    string? ParentId = null,
    string? List = null,
    IReadOnlyDictionary<string, string>? Parameters = null,
    string? Name = null,
    string? Field = null,
    string? Value = null,
    string? Column = null,
    int? Index = null);
