using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

// The macro list's order command: the page sends the complete order it just drew. Keeping it beside
// the other list commands would push the already dense project bridge over the size line.
public sealed partial class ProjectBridge
{
    private Task<object?> Reorder(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("names", out var namesNode)
            || namesNode.ValueKind is not JsonValueKind.Array)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));
        }

        var names = namesNode.EnumerateArray()
            .Select(item => item.GetString())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        var existing = _workspace.Macros
            .Select(macro => macro.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count != _workspace.Macros.Count
            || names.Count != names.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            || names.Any(name => !existing.Contains(name)))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.BadArgument"));
        }

        _workspace.ReorderMacros(names);
        return Task.FromResult<object?>(null);
    }
}
