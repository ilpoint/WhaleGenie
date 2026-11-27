using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>
/// One edit of the callable parameter/result declarations. Family is "parameter" or "result";
/// kind is add/patch/remove/move/fieldAdd/fieldPatch/fieldRemove.
/// </summary>
[BridgeContract]
[BridgeRequest]
public sealed record EditorDeclarationCommand(
    string Family,
    string Kind,
    int? Index = null,
    string? Field = null,
    string? Value = null,
    int? FieldIndex = null,
    int? Offset = null,
    IReadOnlyList<EditorValuePartSnapshot>? Parts = null);
