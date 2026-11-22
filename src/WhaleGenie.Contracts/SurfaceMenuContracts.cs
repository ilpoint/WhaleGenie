using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One occupied cell of a menu page: which macro, where, and with what icon.</summary>
[BridgeContract]
public sealed record SurfaceMenuCell(int Row, int Column, string EntryId, string MacroName,
    string? IconKind, string? IconValue);

/// <summary>One four-by-three menu page: the common pages and the context pages are shaped alike.</summary>
[BridgeContract]
public sealed record SurfaceMenuPage(string PageId, bool IsDefault, string RuleSummary,
    IReadOnlyList<SurfaceMenuCell> Cells);

/// <summary>
/// The whole menu as the project has it right now: the common pages, and the context pages that fit
/// the window in front. Paging, search, hover and drag are the page's own state; this is only what
/// the program knows.
/// </summary>
[BridgeContract]
public sealed record SurfaceMenuSnapshot(IReadOnlyList<SurfaceMenuPage> Common,
    IReadOnlyList<SurfaceMenuPage> Context);
