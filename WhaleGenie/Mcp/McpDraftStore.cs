using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>What validation has to say about one draft, before it can be applied.</summary>
public sealed record McpDraftValidation(
    bool HasErrors, IReadOnlyList<string> Messages);

/// <summary>
/// A copy of the project that an MCP client is editing. It is not the project the window shows:
/// nothing reaches the interface until apply, and nothing reaches the package until save.
/// </summary>
public sealed class McpDraft
{
    private readonly object _gate = new();

    internal McpDraft(McpProjectSnapshot basis)
    {
        Basis = basis;
        Macros = [.. basis.Macros];
        Globals = [.. basis.Globals];
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    public McpProjectSnapshot Basis { get; }

    public List<MacroItem> Macros { get; }

    public List<VariableDefinition> Globals { get; }

    public bool Applied { get; private set; }

    public IReadOnlyList<MacroItem> MacroSnapshot()
    {
        lock (_gate)
        {
            return [.. Macros];
        }
    }

    public MacroItem? Find(string name)
    {
        lock (_gate)
        {
            return Macros.FirstOrDefault(macro =>
                string.Equals(macro.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Adds or replaces one macro by name. The macro JSON is the same shape the package uses.</summary>
    public MacroItem Put(JsonObject json)
    {
        var macro = MacroItem.FromJson(json);
        macro.Name = macro.Name.Trim();
        if (macro.Name.Length == 0)
        {
            throw new McpException("The macro has no name.");
        }

        lock (_gate)
        {
            var existing = Macros.FindIndex(candidate =>
                string.Equals(candidate.Name.Trim(), macro.Name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                Macros[existing] = macro;
            }
            else
            {
                Macros.Add(macro);
            }
        }

        return macro;
    }

    public void Delete(string name)
    {
        lock (_gate)
        {
            var removed = Macros.RemoveAll(macro =>
                string.Equals(macro.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                throw new McpException($"There is no macro named \"{name}\".");
            }
        }
    }

    /// <summary>Replaces the package's global declarations. Values are not run here; only the declarations change.</summary>
    public void SetGlobals(JsonArray array)
    {
        var globals = new List<VariableDefinition>();
        foreach (var item in array)
        {
            if (item is not JsonObject variable)
            {
                throw new McpException("Every global has to be an object.");
            }

            var name = variable["name"]?.GetValue<string>()?.Trim() ?? string.Empty;
            if (name.Length == 0)
            {
                throw new McpException("A global has no name.");
            }

            globals.Add(new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = variable["type"]?.GetValue<string>() ?? "text",
                DefaultValue = variable["default"]?.GetValue<string>() ?? string.Empty,
                Description = variable["description"]?.GetValue<string>() ?? string.Empty,
            });
        }

        lock (_gate)
        {
            Globals.Clear();
            Globals.AddRange(globals);
        }
    }

    internal void MarkApplied()
    {
        lock (_gate)
        {
            Applied = true;
        }
    }
}

/// <summary>All drafts of one running MCP server. A server is stopped as a whole; so is this store.</summary>
public sealed class McpDraftStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, McpDraft> _drafts = [];

    public McpDraft Create(McpProjectSnapshot basis)
    {
        var draft = new McpDraft(basis);
        lock (_gate)
        {
            _drafts[draft.Id] = draft;
        }

        return draft;
    }

    public McpDraft Get(string id)
    {
        lock (_gate)
        {
            return _drafts.TryGetValue(id, out var draft)
                ? draft
                : throw new McpException($"There is no draft \"{id}\".");
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _drafts.Remove(id);
        }
    }

    /// <summary>Checks every macro, including the call graph between them.</summary>
    public static McpDraftValidation Validate(McpDraft draft)
    {
        var messages = new List<string>();
        var macros = draft.MacroSnapshot();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in macros)
        {
            var name = macro.Name.Trim();
            if (name.Length == 0)
            {
                messages.Add("A macro has no name.");
                continue;
            }

            if (!names.Add(name))
            {
                messages.Add($"Two macros are named \"{name}\".");
            }
        }

        var callables = macros.Select(macro => macro.Callable).ToList();
        foreach (var macro in macros)
        {
            foreach (var problem in MacroCheck.DeclarationProblems(macro.Callable))
            {
                messages.Add($"{macro.Name}: {problem}");
            }

            foreach (var problem in MacroCheck.Inspect(macro.Steps, macro.Callable, callables))
            {
                messages.Add($"{macro.Name}: {problem.Message}");
            }
        }

        return new McpDraftValidation(messages.Count > 0, messages);
    }
}
