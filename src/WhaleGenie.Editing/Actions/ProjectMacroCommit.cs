using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>What the project did with the macro an editor handed back.</summary>
internal enum MacroCommitOutcome
{
    /// <summary>A new macro joined the list.</summary>
    Added,

    /// <summary>The target macro was replaced by the edited one.</summary>
    Replaced,

    /// <summary>The draft said exactly what the project already held.</summary>
    Unchanged,

    /// <summary>The target macro is no longer in the list; the draft was not written.</summary>
    TargetGone,
}

/// <summary>The answer of one commit, with the instance the document is bound to afterwards.</summary>
internal readonly record struct MacroCommitResult(MacroCommitOutcome Outcome, MacroItem? Target)
{
    /// <summary>True when the project holds the draft after this commit.</summary>
    public bool Accepted
        => Outcome is MacroCommitOutcome.Added or MacroCommitOutcome.Replaced
            or MacroCommitOutcome.Unchanged;
}

/// <summary>
/// What the project does with the macro an editor hands back: it joins the list, replaces the one
/// that was edited, or is left alone because it says exactly what the project already holds.
/// </summary>
/// <remarks>
/// This is state and a rule, not an event handler. The editor can close long after the window that
/// opened it has put it aside, and the macro it replaces can be gone by then; the answer belongs
/// beside the target and the macros lifted during the session rather than in the window's fields.
/// The window supplies the three things that only it can do: stop the run of the macro being
/// replaced, colour the list, and nudge the crash draft.
/// </remarks>
internal sealed class ProjectMacroCommit(
    ProjectWorkspace project,
    MacroItem? target,
    MacroView newView,
    Action<MacroItem> stopRunning,
    Action refreshRunning,
    Action followRecovery)
{
    private readonly HashSet<MacroItem> _lifted = [];

    /// <summary>The macro in the list this session replaces, or null until a new one has joined.</summary>
    public MacroItem? Target { get; private set; } = target;

    /// <summary>
    /// Takes what the editor ended with and answers whether the project took it. A macro that has
    /// left the list cannot be written back: a draft applied from MCP replaces the list's instances,
    /// and replacing one that is no longer there would silently drop the edit. False leaves the
    /// work in the editor, which shows why and keeps its draft.
    /// </summary>
    public MacroCommitResult Apply(MacroDraft draft, IReadOnlyList<MacroItem> lifted)
    {
        var current = Target;
        if (current is { } targetInList && !project.Macros.Contains(targetInList))
        {
            return new MacroCommitResult(MacroCommitOutcome.TargetGone, null);
        }

        // A stretch of steps lifted into a macro of its own during the session is real work: it
        // joins the project together with the macro that calls it, because the call is only in the
        // list that is arriving now.
        foreach (var item in lifted)
        {
            if (_lifted.Add(item))
            {
                project.AddMacro(item);
            }
        }

        if (current is null)
        {
            StoreTriggerSettings(draft.Layouts);
            var added = project.AddMacro(draft.Macro, draft.Entries);
            PlaceNewMacro(newView, added, project);
            Target = added;
            followRecovery();
            return new MacroCommitResult(MacroCommitOutcome.Added, added);
        }

        // Saving an unchanged macro is not a project change. This also makes the close after a
        // successful save a no-op rather than a second insertion or replacement.
        if (Same(project, current, draft))
        {
            return new MacroCommitResult(MacroCommitOutcome.Unchanged, current);
        }

        // The edited macro replaces the one the trigger may be running.
        stopRunning(current);
        StoreTriggerSettings(draft.Layouts);
        if (!project.ReplaceMacro(current, draft.Macro, draft.Entries))
        {
            return new MacroCommitResult(MacroCommitOutcome.TargetGone, null);
        }

        Target = draft.Macro;
        refreshRunning();

        // The editor has just let its draft go, so the list is the only copy of this macro: it
        // goes into the snapshot now rather than waiting for the next turn of the timer.
        followRecovery();
        return new MacroCommitResult(MacroCommitOutcome.Replaced, draft.Macro);
    }

    /// <summary>
    /// Puts a macro that was created from the menu or the wheel view where that view keeps things.
    /// It happens after the save because a position names the macro, and a macro is named by the
    /// editor. The key, window and string views already brought their own entry with them.
    /// </summary>
    internal static void PlaceNewMacro(MacroView view, MacroItem macro, ProjectWorkspace project)
    {
        switch (view)
        {
            case MacroView.Menu when project.Triggers.FirstFreeMenuCell() is { } cell:
                project.Triggers.Entries.Add(new TriggerEntry
                {
                    Spec = new MenuSpec(cell.PageId, cell.Row, cell.Column),
                    MacroName = macro.Name,
                    IsEnabled = true,
                });
                break;
            case MacroView.Wheel when project.Triggers.Wheel.Global.FirstEmptySlot() is { } slot:
                slot.Macro = macro.Name;
                project.Triggers.NotifyChanged();
                break;
        }
    }

    /// <summary>
    /// Whether the editor brought back the very same macro, triggers and layouts. The editor builds
    /// the macro again as it hands it over, so this is a question about what the macro says rather
    /// than about which object it is; two macros that read the same are written the same way.
    /// </summary>
    private static bool Same(ProjectWorkspace project, MacroItem target, MacroDraft draft)
        => string.Equals(target.ToJson().ToJsonString(), draft.Macro.ToJson().ToJsonString(),
            StringComparison.Ordinal)
           && string.Equals(TriggerJson(project.Triggers.ForMacro(target.Name)),
               TriggerJson(draft.Entries), StringComparison.Ordinal)
           && string.Equals(LayoutJson(project.Triggers.InterfaceLayouts),
               LayoutJson(draft.Layouts), StringComparison.Ordinal);

    private static string TriggerJson(IReadOnlyList<TriggerEntry> entries)
    {
        var array = new JsonArray();
        foreach (var entry in entries)
        {
            array.Add(entry.ToJson());
        }

        return array.ToJsonString();
    }

    private static string LayoutJson(IReadOnlyList<InterfaceLayout> layouts)
    {
        var array = new JsonArray();
        foreach (var layout in layouts)
        {
            array.Add(layout.ToJson());
        }

        return array.ToJsonString();
    }

    private void StoreTriggerSettings(IReadOnlyList<InterfaceLayout> layouts)
    {
        foreach (var layout in layouts)
        {
            project.Triggers.StoreInterfaceHotkey(layout.Window, layout.ToggleHotkey);
        }
    }
}
