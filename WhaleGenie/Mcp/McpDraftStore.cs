using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>One thing validation says about a draft. It names the macro and, where the problem is
/// about one step, the step id, so a client can point at the row instead of re-reading the macro.</summary>
/// <summary>
/// One thing validation found. <paramref name="Code"/> is what a client branches on: the sentence in
/// <paramref name="Message"/> is in whatever language the interface is set to, so comparing it is
/// not a way to recognise a problem.
/// </summary>
public sealed record McpProblem(string Macro, string? StepId, string Code, string Message);

/// <summary>
/// What validation says about one draft. Blocking problems make a macro that cannot run and stop
/// apply; hints are worth reading and block nothing.
/// </summary>
public sealed record McpDraftValidation(
    bool Ok, IReadOnlyList<McpProblem> Blocking, IReadOnlyList<McpProblem> Hints);

/// <summary>
/// What one write put in the draft: the macro as it now stands, and the ids the repair had to move
/// because the macro arrived with two steps sharing one. The client is told so it can see that the
/// names it wrote are not the names that were kept.
/// </summary>
public sealed record McpPut(MacroItem Macro, IReadOnlyList<StepIds.Rename> Renamed);

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
        Triggers = basis.Triggers.Copy();
        Triggers.Attach(Macros);
        Globals = [.. basis.Globals];
    }

    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>When the draft was taken, which is what tells two drafts of one project apart.</summary>
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;

    public McpProjectSnapshot Basis { get; }

    public List<MacroItem> Macros { get; }

    public ProjectTriggers Triggers { get; }

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

    /// <summary>Adds or replaces one macro by name, and says which ids the repair moved.</summary>
    public McpPut Put(JsonObject json)
    {
        var macro = MacroItem.FromJson(json);
        var renamed = McpPatches.Settle(macro);
        macro.Name = macro.Name.Trim();
        if (macro.Name.Length == 0)
        {
            throw new McpFailure(McpErrors.BadArgument, "The macro has no name.",
                "Every macro needs a name: it is how apply, get_macro and the trigger list find it.");
        }

        lock (_gate)
        {
            var existing = Macros.FindIndex(candidate =>
                string.Equals(candidate.Name.Trim(), macro.Name, StringComparison.OrdinalIgnoreCase));

            // A macro sent with no trigger key is a macro whose steps changed, not one whose
            // triggers did: the triggers it had come along, taken from the project's own list.
            if (!json.ContainsKey("triggers") && existing >= 0)
            {
                macro.Triggers.AddRange(Triggers.ForMacro(macro.Name).Select(entry => entry.Copy()));
            }

            if (existing >= 0)
            {
                Macros[existing] = macro;
            }
            else
            {
                Macros.Add(macro);
            }

            // A macro sent with no trigger key keeps the triggers the project already had — that is
            // what "I only changed the steps" means. A macro sent with an empty trigger list is a
            // different statement: it says this macro has none, so the old ones come out.
            if (json.ContainsKey("triggers") || macro.Triggers.Count > 0)
            {
                Triggers.ReplaceMacro(macro.Name, macro.Triggers.Select(entry => entry.Copy()));
            }
        }

        McpAudit.Note("draft.put", $"{macro.Name} ({macro.Steps.Count} steps)");
        return new McpPut(macro, renamed);
    }

    public void Delete(string name)
    {
        lock (_gate)
        {
            var removed = Macros.RemoveAll(macro =>
                string.Equals(macro.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
            {
                throw new McpFailure(McpErrors.NoSuchMacro, $"There is no macro named \"{name}\".",
                    "Call whalegenie_list_macros to see the names this draft holds.",
                    retryable: true, details: new { name });
            }

            Triggers.RemoveMacro(name);
            McpAudit.Note("draft.delete", name);
        }
    }

    /// <summary>
    /// The global declarations this draft carries, in the same shape <see cref="SetGlobals"/> takes.
    /// Reading them first is what keeps a whole-list write from quietly dropping the ones nobody
    /// mentioned.
    /// </summary>
    public IReadOnlyList<object> GlobalList()
    {
        lock (_gate)
        {
            return [.. Globals.Select(variable => new
            {
                name = variable.Name,
                type = variable.Type,
                @default = variable.DefaultValue,
                description = variable.Description,
                persistent = variable.PersistValue,
            })];
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
                throw new McpFailure(McpErrors.BadArgument, "Every global has to be an object.",
                    "A declaration is {name, type, default, description}.");
            }

            var name = Text(variable["name"]).Trim();
            if (name.Length == 0)
            {
                throw new McpFailure(McpErrors.BadArgument, "A global has no name.",
                    "Every declaration needs a name; it is the word a macro reads as $name.");
            }

            globals.Add(new VariableDefinition
            {
                Name = name,
                Scope = VariableScope.Global,
                Type = Default(Text(variable["type"]), "text"),
                DefaultValue = Text(variable["default"]),
                Description = Text(variable["description"]),
            });
        }

        lock (_gate)
        {
            Globals.Clear();
            Globals.AddRange(globals);
        }

        McpAudit.Note("draft.globals", $"{globals.Count} declarations");
    }

    /// <summary>
    /// A string as it was written, or empty. A field of the wrong type is a shape the caller can fix
    /// and not a reason to stop with a framework exception.
    /// </summary>
    private static string Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static string Default(string text, string fallback) => text.Length > 0 ? text : fallback;

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
    /// <summary>
    /// How many drafts one session may hold. Every draft is a deep copy of the project, and a client
    /// that opens one on each turn without ever discarding one is a session that grows until it ends.
    /// </summary>
    public const int MaxDrafts = 8;

    private readonly object _gate = new();
    private readonly Dictionary<string, McpDraft> _drafts = [];

    /// <summary>
    /// Opens a draft, dropping an applied one when the session is full. A draft that was applied has
    /// already reached the project, so losing it costs a list entry; a draft that was never applied
    /// is work, so the room is made by refusing rather than by throwing it away behind the client's
    /// back.
    /// </summary>
    public (McpDraft Draft, IReadOnlyList<string> Dropped) Create(McpProjectSnapshot basis)
    {
        var draft = new McpDraft(basis);
        List<string> dropped = [];
        lock (_gate)
        {
            while (_drafts.Count >= MaxDrafts)
            {
                var spent = _drafts.Values
                    .Where(candidate => candidate.Applied)
                    .OrderBy(candidate => candidate.CreatedAt)
                    .FirstOrDefault();
                if (spent is null)
                {
                    throw new McpFailure(McpErrors.TooManyDrafts,
                        $"This session already holds {_drafts.Count} drafts and none has been applied.",
                        "Call whalegenie_discard for a draft that is no longer needed, or apply one so it can be dropped, then open the project again.",
                        retryable: true,
                        details: new
                        {
                            limit = MaxDrafts,
                            drafts = _drafts.Values
                                .OrderBy(entry => entry.CreatedAt)
                                .Select(entry => new { id = entry.Id, entry.Applied }),
                        });
                }

                _drafts.Remove(spent.Id);
                dropped.Add(spent.Id);
            }

            _drafts[draft.Id] = draft;
        }

        if (dropped.Count > 0)
        {
            McpAudit.Note("draft.drop", string.Join(", ", dropped) + " (applied)");
        }

        return (draft, dropped);
    }

    public McpDraft Get(string id)
    {
        lock (_gate)
        {
            return _drafts.TryGetValue(id, out var draft)
                ? draft
                : throw new McpFailure(McpErrors.DraftNotFound, $"There is no draft \"{id}\".",
                    "A draft is dropped when the server stops; call whalegenie_open_project for a new one.",
                    retryable: true, details: new { draftId = id });
        }
    }

    /// <summary>Drops a draft, and says whether there was one: a client that typed the wrong id
    /// should hear about it rather than be told the discard worked.</summary>
    public bool Remove(string id)
    {
        lock (_gate)
        {
            return _drafts.Remove(id);
        }
    }

    /// <summary>Every draft still open, newest first: the list a person reads to see what is pending.</summary>
    public IReadOnlyList<McpDraft> All()
    {
        lock (_gate)
        {
            return [.. _drafts.Values.OrderByDescending(draft => draft.CreatedAt)];
        }
    }

    /// <summary>How many drafts a client is holding open right now.</summary>
    public int Open
    {
        get
        {
            lock (_gate)
            {
                return _drafts.Count;
            }
        }
    }

    /// <summary>Checks every macro, including the call graph between them.</summary>
    public static McpDraftValidation Validate(McpDraft draft)
    {
        var blocking = new List<McpProblem>();
        var hints = new List<McpProblem>();
        var macros = draft.MacroSnapshot();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in macros)
        {
            var name = macro.Name.Trim();
            if (name.Length == 0)
            {
                blocking.Add(new McpProblem(string.Empty, null, "MacroNameMissing", "A macro has no name."));
                continue;
            }

            if (!names.Add(name))
            {
                blocking.Add(new McpProblem(name, null, "MacroNameDuplicate",
                    $"Two macros are named \"{name}\"."));
            }
        }

        var callables = macros.Select(macro => macro.Callable).ToList();
        foreach (var macro in macros)
        {
            // The editor has always drawn these apart: a fault makes the macro impossible to call,
            // a warning is worth reading while it is being written. Applying follows the same line.
            foreach (var fault in MacroCheck.DeclarationFaultProblems(macro.Callable))
            {
                blocking.Add(new McpProblem(macro.Name, null, fault.Code, fault.Message));
            }

            foreach (var warning in MacroCheck.DeclarationWarnings(macro.Callable))
            {
                hints.Add(new McpProblem(macro.Name, null, "DeclarationWarning", warning));
            }

            foreach (var problem in MacroCheck.Inspect(macro.Steps, macro.Callable, callables))
            {
                // The editor draws both kinds on the step; only the ones that cannot work stops an
                // apply. A draft carrying a hint is a draft somebody may still be halfway through
                // writing, and refusing it would teach the client to fight the check.
                var written = new McpProblem(macro.Name,
                    problem.Step.Id.Length > 0 ? problem.Step.Id : null,
                    problem.Code, problem.Message);

                (problem.Severity == MacroProblemSeverity.Blocking ? blocking : hints).Add(written);
            }
        }

        return new McpDraftValidation(blocking.Count == 0, blocking, hints);
    }
}
