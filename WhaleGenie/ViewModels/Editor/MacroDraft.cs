using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// What the macro editor hands the project when it saves: the macro body, the trigger entries that
/// point at it, and the interface layouts it was showing. They travel together because the project
/// writes them as one change — a macro that arrives without the entries the editor made would lose
/// what starts it.
/// </summary>
public sealed record MacroDraft(MacroItem Macro, IReadOnlyList<TriggerEntry> Entries,
    IReadOnlyList<InterfaceLayout> Layouts);
