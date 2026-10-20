using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>One runnable or editable cell resolved from the project's menu trigger entries.</summary>
public sealed record MenuRuntimeCell(
    int Row,
    int Column,
    string Label,
    MenuIconReference? Icon,
    TriggerEntry Entry,
    MacroItem Macro);

/// <summary>One four-column by three-row common or context page.</summary>
public sealed record MenuRuntimePage(
    string PageId,
    bool IsDefault,
    string RuleSummary,
    IReadOnlyList<MenuRuntimeCell> Cells);

/// <summary>
/// Immutable menu data handed to the floating menu window. Common pages scroll horizontally;
/// the context page is chosen once from the foreground window and the default context page.
/// </summary>
public sealed record MenuRuntimeLayout(
    IReadOnlyList<MenuRuntimePage> CommonPages,
    IReadOnlyList<MenuRuntimePage> ContextPages);
