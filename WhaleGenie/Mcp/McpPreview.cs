using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>One step of a macro, named the way an insert or a move names it.</summary>
public sealed record McpStepRef(string Id, string Type, string Path);

/// <summary>One step that is in both versions of a macro but no longer says the same thing.</summary>
public sealed record McpStepChange(McpStepRef Step, IReadOnlyList<string> Parameters);

/// <summary>
/// What one macro looks like after applying: new, gone, or the same name with a different body. A
/// changed macro says which steps came, went, moved or changed, so the person does not have to diff
/// two 40-step macros by eye.
/// </summary>
public sealed record McpMacroChange(
    string Name,
    string Change,
    int Steps,
    int StepsBefore,
    IReadOnlyList<McpStepRef> AddedSteps,
    IReadOnlyList<McpStepRef> RemovedSteps,
    IReadOnlyList<McpStepRef> MovedSteps,
    IReadOnlyList<McpStepChange> ChangedSteps);

/// <summary>One global declaration that would be added, changed or removed.</summary>
public sealed record McpGlobalChange(string Name, string Change);

/// <summary>How many macros each way, which is the line a person reads first.</summary>
public sealed record McpPreviewTotals(int Added, int Changed, int Removed, int Unchanged);

/// <summary>
/// What applying one draft would change, worked out from the copy it was taken from and the copy the
/// client has been editing. Nothing here touches the project: this is the answer to "what am I about
/// to agree to", which is the question apply used to answer only after it had already written.
/// </summary>
public sealed record McpPreviewResult(
    IReadOnlyList<McpMacroChange> Macros,
    IReadOnlyList<McpGlobalChange> Globals,
    McpPreviewTotals Totals);

/// <summary>
/// Works out the difference between a draft and the project it was taken from. Steps are matched by
/// their id, which is the same id a patch and an outline use, so "this step changed" points at the
/// row the client already knows rather than at a position that shifting has already moved.
/// </summary>
public static class McpPreview
{
    public static McpPreviewResult Of(McpDraft draft)
    {
        var before = draft.Basis.Macros.ToDictionary(macro => macro.Name.Trim(),
            StringComparer.OrdinalIgnoreCase);
        var after = draft.MacroSnapshot().ToDictionary(macro => macro.Name.Trim(),
            StringComparer.OrdinalIgnoreCase);

        var macros = new List<McpMacroChange>();
        var added = 0;
        var changed = 0;
        var removed = 0;

        foreach (var (name, macro) in after.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!before.TryGetValue(name, out var was))
            {
                added++;
                macros.Add(new McpMacroChange(name, "added", macro.Steps.Count, 0,
                    [], [], [], []));
                continue;
            }

            if (Same(was, macro))
            {
                continue;
            }

            changed++;
            macros.Add(Compare(name, was, macro));
        }

        foreach (var (name, macro) in before.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (after.ContainsKey(name))
            {
                continue;
            }

            removed++;
            macros.Add(new McpMacroChange(name, "removed", 0, macro.Steps.Count, [], [], [], []));
        }

        return new McpPreviewResult(
            macros,
            Globals(draft.Basis.Globals, draft.Globals),
            new McpPreviewTotals(added, changed, removed, after.Count - added - changed));
    }

    /// <summary>True when two versions of a macro are the same file, triggers and sub-macros included.</summary>
    private static bool Same(MacroItem was, MacroItem now)
        => string.Equals(was.ToJson().ToJsonString(), now.ToJson().ToJsonString(), StringComparison.Ordinal);

    private static McpMacroChange Compare(string name, MacroItem was, MacroItem now)
    {
        var before = Steps(was);
        var after = Steps(now);

        var added = after.Keys.Where(id => !before.ContainsKey(id))
            .OrderBy(id => after[id].Path, StringComparer.Ordinal)
            .Select(id => new McpStepRef(id, after[id].Type, after[id].Path)).ToList();
        var removed = before.Keys.Where(id => !after.ContainsKey(id))
            .OrderBy(id => before[id].Path, StringComparer.Ordinal)
            .Select(id => new McpStepRef(id, before[id].Type, before[id].Path)).ToList();

        var moved = new List<McpStepRef>();
        var changed = new List<McpStepChange>();
        foreach (var id in after.Keys.Where(before.ContainsKey).OrderBy(id => after[id].Path,
                     StringComparer.Ordinal))
        {
            var step = after[id];
            var was_ = before[id];
            if (string.Equals(step.Json, was_.Json, StringComparison.Ordinal))
            {
                // The same step in a different place is a move, not a change: saying it changed
                // would send the client looking for a parameter that is exactly as it was.
                if (!string.Equals(step.Path, was_.Path, StringComparison.Ordinal))
                {
                    moved.Add(new McpStepRef(id, step.Type, step.Path));
                }

                continue;
            }

            changed.Add(new McpStepChange(new McpStepRef(id, step.Type, step.Path),
                Differing(was_.Json, step.Json)));
        }

        return new McpMacroChange(name, "changed", now.Steps.Count, was.Steps.Count,
            added, removed, moved, changed);
    }

    /// <summary>Every step of a macro, main body and sub-macros, keyed by the id a patch would name.</summary>
    private static Dictionary<string, (string Type, string Path, string Json)> Steps(MacroItem macro)
    {
        var steps = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
        Walk(macro.Steps, string.Empty, "main", steps);
        foreach (var sub in macro.SubMacros)
        {
            Walk(sub.Steps, string.Empty, "sub:" + sub.Name, steps);
        }

        return steps;
    }

    private static void Walk(IReadOnlyList<MacroStep> steps, string prefix, string scope,
        Dictionary<string, (string Type, string Path, string Json)> into)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            var step = steps[at];
            var path = prefix.Length == 0
                ? at.ToString(CultureInfo.InvariantCulture)
                : prefix + "." + at.ToString(CultureInfo.InvariantCulture);
            if (step.Id.Length > 0)
            {
                into[step.Id] = (step.Type, path, step.ToJson().ToJsonString());
            }

            foreach (var list in step.StepLists)
            {
                Walk(list.Steps, path + "." + list.Name, scope, into);
            }
        }
    }

    /// <summary>
    /// Which parts of a step are not what they were: the parameter names where they differ, and
    /// anything else the step writes besides its id.
    /// </summary>
    private static IReadOnlyList<string> Differing(string was, string now)
    {
        if (JsonNode.Parse(was) is not JsonObject before || JsonNode.Parse(now) is not JsonObject after)
        {
            return [];
        }

        var names = new List<string>();
        var wasParams = before["params"] as JsonObject ?? [];
        var nowParams = after["params"] as JsonObject ?? [];
        foreach (var name in nowParams.Select(pair => pair.Key)
                     .Union(wasParams.Select(pair => pair.Key), StringComparer.Ordinal)
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!string.Equals(wasParams[name]?.ToJsonString(), nowParams[name]?.ToJsonString(),
                    StringComparison.Ordinal))
            {
                names.Add(name);
            }
        }

        foreach (var name in after.Select(pair => pair.Key)
                     .Union(before.Select(pair => pair.Key), StringComparer.Ordinal)
                     .Where(name => name is not ("id" or "params" or "type"))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!string.Equals(before[name]?.ToJsonString(), after[name]?.ToJsonString(),
                    StringComparison.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyList<McpGlobalChange> Globals(
        IReadOnlyList<VariableDefinition> was, IReadOnlyList<VariableDefinition> now)
    {
        var before = was.ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var after = now.ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var changes = new List<McpGlobalChange>();

        foreach (var (name, variable) in after.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!before.TryGetValue(name, out var had))
            {
                changes.Add(new McpGlobalChange(name, "added"));
            }
            else if (!SameDefinition(had, variable))
            {
                changes.Add(new McpGlobalChange(name, "changed"));
            }
        }

        changes.AddRange(before.Keys.Where(name => !after.ContainsKey(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Select(name => new McpGlobalChange(name, "removed")));

        return changes;
    }

    private static bool SameDefinition(VariableDefinition was, VariableDefinition now)
        => string.Equals(was.Type, now.Type, StringComparison.Ordinal)
            && string.Equals(was.DefaultValue, now.DefaultValue, StringComparison.Ordinal)
            && string.Equals(was.Description, now.Description, StringComparison.Ordinal)
            && was.Scope == now.Scope
            && was.PersistValue == now.PersistValue;
}
