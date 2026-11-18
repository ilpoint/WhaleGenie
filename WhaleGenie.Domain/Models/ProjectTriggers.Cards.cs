using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

// What the macro cards say about the triggers that point at them.
public sealed partial class ProjectTriggers
{
    /// <summary>
    /// Writes the display lines every card shows. This is a projection, not a second copy: the
    /// entries here are the only triggers a project has, and the card only keeps the sentence it
    /// was last given.
    /// </summary>
    public void Project(IReadOnlyList<MacroItem> macros)
    {
        foreach (var macro in macros)
        {
            var entries = ForMacro(macro.Name);
            macro.ShowTriggers(
                entries.Count == 0
                    ? Strings.Get("Trigger.Unset")
                    : string.Join(" · ", entries.Select(entry => entry.Summary)),
                LoopWords(entries),
                entries.Count == 0 ? Strings.Get("Common.None") : entries[0].Summary);
        }
    }

    /// <summary>
    /// The ways the key and colour entries repeat. A schedule starts the macro rather than holding
    /// it, so it contributes no word here.
    /// </summary>
    private static string LoopWords(IReadOnlyList<TriggerEntry> entries)
    {
        var loops = entries
            .Select(entry => entry.Spec switch
            {
                KeySpec key => key.Loop,
                ColorSpec colour => colour.Loop,
                _ => (MacroLoop?)null,
            })
            .OfType<MacroLoop>()
            .Distinct()
            .Select(loop => Strings.Get(loop switch
            {
                MacroLoop.Hold => "Loop.Hold",
                MacroLoop.Press => "Loop.Press",
                MacroLoop.Release => "Loop.Release",
                _ => "Loop.Toggle",
            }));

        return string.Join(" · ", loops);
    }
}
