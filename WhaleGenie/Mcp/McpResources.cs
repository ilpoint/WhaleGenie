using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>
/// What a client may read and keep, rather than ask for again. The catalogue, the project and any
/// one macro are pages here: a client that can cache a resource reads one once and works from it,
/// while the same thing asked for as a tool answer has to be asked for at every turn — and a long
/// macro is written over many turns.
/// </summary>
/// <remarks>
/// The resources say what the tools say, because both are read off the same objects, so a client may
/// use either. The tools stay for a client that cannot read resources. Every project page carries
/// the revision: within one revision the page does not change, which is what makes keeping it safe,
/// and a page whose revision is behind is the signal to read it again.
/// </remarks>
[McpServerResourceType]
public sealed class McpResources(McpLiveProject live)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,

        // Names and descriptions are Chinese on a Chinese machine, and an escaped answer is twice
        // the size and unreadable in a log.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Every action this build knows: the key, what it is called, the category, whether it is hidden,
    /// and the names of its parameters. The parameter names are here because that is what a client
    /// memorises; what each one means is in the single-action resource.
    /// </summary>
    [McpServerResource(UriTemplate = "whalegenie://actions", Name = "Action catalogue",
        Title = "Action catalogue", MimeType = "application/json")]
    [Description("Every action this build knows: key, name, category, whether it is hidden, and the names of its parameters. Read it once and keep it; it changes only when the program is updated.")]
    public async Task<string> Catalogue()
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var actions = ActionSemanticsCatalog.All.Select(action => new
            {
                key = action.Key,
                name = ActionCatalog.Find(action.Key)?.LocalName ?? action.Key,
                category = action.Category.ToString(),
                hidden = action.Hidden,
                parameters = action.Parameters.Select(parameter => parameter.Name).ToList(),
            }).ToList();

            return JsonSerializer.Serialize(new { total = actions.Count, actions }, Json);
        });

    /// <summary>
    /// What the catalogue adds up to, by group: how many actions, parameters, results and how many
    /// actions hand anything back. It is the shape a client reads before deciding what is worth
    /// looking at, and the numbers are the ones the action matrix measures, so a change shows up in
    /// both places at once.
    /// </summary>
    [McpServerResource(UriTemplate = "whalegenie://action-matrix", Name = "Action matrix",
        Title = "Action matrix", MimeType = "application/json")]
    [Description("How many actions, parameters and results the catalogue holds, and the same counts by group. Useful for deciding what to look at; the per-action detail is in whalegenie://actions/{key}.")]
    public async Task<string> Matrix()
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var all = ActionSemanticsCatalog.All;
            var groups = all
                .GroupBy(action => action.Category)
                .OrderBy(group => group.Key.ToString(), StringComparer.Ordinal)
                .Select(group => new
                {
                    category = group.Key.ToString(),
                    actions = group.Count(),
                    parameters = group.Sum(action => action.Parameters.Count),
                    results = group.Sum(action => action.Returns.Count),
                    withReturns = group.Count(action => action.Returns.Count > 0),
                    sendsInput = group.Count(action => action.SendsInput),
                    looksAtScreen = group.Count(action => action.LooksAtScreen),
                    waits = group.Count(action => action.Waits),
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                total = all.Count,
                parameters = all.Sum(action => action.Parameters.Count),
                results = all.Sum(action => action.Returns.Count),
                withReturns = all.Count(action => action.Returns.Count > 0),
                sendsInput = all.Count(action => action.SendsInput),
                looksAtScreen = all.Count(action => action.LooksAtScreen),
                waits = all.Count(action => action.Waits),
                byCategory = groups,
            }, Json);
        });

    /// <summary>
    /// One action, described the way <c>whalegenie_get_action</c> describes it: reading rules, kinds,
    /// defaults, options, units, ranges, record fields, child lists, row columns and returns.
    /// </summary>
    [McpServerResource(UriTemplate = "whalegenie://actions/{key}", Name = "One action",
        Title = "One action", MimeType = "application/json")]
    [Description("One action, described in full: parameters with their kinds, reading rules, defaults, options, units and ranges, the child lists each accepts, the columns of each table, and what it returns. An unknown key answers with the nearest keys as candidates rather than failing.")]
    public async Task<string> Action(string key)
        => await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (ActionKnowledge.Find(key) is { } action)
            {
                return JsonSerializer.Serialize(action, Json);
            }

            var nearest = ActionKnowledge.Search(key, null, includeHidden: true, offset: 0, limit: 5);
            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = "UnknownAction",
                    message = $"There is no action \"{key}\".",
                    hint = "Read whalegenie://actions for every key this build knows.",
                    candidates = nearest.Actions.Select(candidate => candidate.Key).ToList(),
                },
            }, Json);
        });

    /// <summary>
    /// The open project as one page: how far along it is, the macros it holds, the globals they
    /// read, and the triggers that start them. This is the page a client keeps instead of calling
    /// <c>whalegenie_project_status</c> and <c>whalegenie_list_macros</c> every turn, and the page
    /// that says which macro is worth reading next.
    /// </summary>
    [McpServerResource(UriTemplate = "whalegenie://project", Name = "The open project",
        Title = "The open project", MimeType = "application/json")]
    [Description("The project WhaleGenie has open: its revision, package path, every macro with its step count, arguments, results and trigger count, the global variables, and the triggers that start a macro. Read it once and keep it — the revision is the version of the page, so the same revision answers the same thing and a page behind the current revision is the signal to read it again. Each macro's own page is whalegenie://macros/{name}.")]
    public async Task<string> Project()
    {
        var (snapshot, dirty) = await live.ReadAsync();

        var macros = snapshot.Macros
            .Select(macro => McpOutline.Entry(macro, MacroUri(macro.Name), TriggersOf(snapshot, macro.Name)))
            .ToList();

        var globals = snapshot.Globals
            .Select(variable => new
            {
                name = variable.Name,
                type = variable.Type.ToString(),
                @default = variable.DefaultValue,
                description = variable.Description,
            })
            .ToList();

        var triggers = snapshot.Triggers.Entries
            .Select(entry => new
            {
                kind = entry.Kind.ToString(),
                macro = entry.MacroName,
                summary = entry.Summary,
            })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            revision = snapshot.Revision,
            path = snapshot.Path,
            dirty,
            macros,
            globals,
            triggers,
        }, Json);
    }

    /// <summary>
    /// One macro, as much as a client needs to change it without reading it whole: every step's id,
    /// action and path, the submacros it holds, and what a call to it fills in and hands back.
    /// </summary>
    /// <remarks>
    /// A step's parameters are not here. They are what <c>whalegenie_get_step</c> and
    /// <c>whalegenie_get_macro</c> answer, and putting them on this page would make the page as large
    /// as the macro it describes — which is the cost an outline exists to avoid.
    /// </remarks>
    [McpServerResource(UriTemplate = "whalegenie://macros/{name}", Name = "One macro",
        Title = "One macro", MimeType = "application/json")]
    [Description("One macro of the open project: its revision, its step count, every step as id / action / path / which macro it belongs to, the submacros it holds, and the arguments and results a call to it fills in and asks for. The path is what an insert or a move names as its parent. Step parameters are not here — use whalegenie_get_step for one step and whalegenie_get_macro for the whole document. An unknown name answers with the nearest names as candidates rather than failing.")]
    public async Task<string> Macro(string name)
    {
        var snapshot = await live.SnapshotAsync();
        var wanted = Unescaped(name);
        var macro = snapshot.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase));

        if (macro is null)
        {
            var candidates = snapshot.Macros
                .Select(candidate => candidate.Name)
                .Where(candidate => candidate.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                    || wanted.Contains(candidate.Trim(), StringComparison.OrdinalIgnoreCase))
                .Take(5)
                .ToList();

            return JsonSerializer.Serialize(new
            {
                error = new
                {
                    code = McpErrors.NoSuchMacro,
                    message = $"There is no macro named \"{wanted}\".",
                    hint = "Read whalegenie://project for every macro this project holds.",
                    candidates,
                },
            }, Json);
        }

        return JsonSerializer.Serialize(new
        {
            name = macro.Name,
            revision = snapshot.Revision,
            steps = macro.Steps.Count,
            outline = McpOutline.Of(macro),
            subMacros = macro.SubMacros
                .Select(sub => new { name = sub.Name, steps = sub.Steps.Count })
                .ToList(),
            arguments = McpOutline.Arguments(macro),
            results = McpOutline.Results(macro),
            triggers = snapshot.Triggers.Entries
                .Where(entry => Same(entry.MacroName, macro.Name))
                .Select(entry => new { kind = entry.Kind.ToString(), summary = entry.Summary })
                .ToList(),
        }, Json);
    }

    /// <summary>Where one macro's page lives, the name escaped so it survives being a path.</summary>
    public static string MacroUri(string name) => "whalegenie://macros/" + Uri.EscapeDataString(name.Trim());

    /// <summary>
    /// The name a URI carried, undoing the escaping a URI needed. A name that was never escaped comes
    /// back as it went in, so both forms reach the same macro.
    /// </summary>
    private static string Unescaped(string? name)
    {
        var text = (name ?? string.Empty).Trim();
        try
        {
            return Uri.UnescapeDataString(text).Trim();
        }
        catch (UriFormatException)
        {
            return text;
        }
    }

    private static int TriggersOf(McpProjectSnapshot snapshot, string macro)
        => snapshot.Triggers.Entries.Count(entry => Same(entry.MacroName, macro));

    private static bool Same(string? left, string? right)
        => string.Equals((left ?? string.Empty).Trim(), (right ?? string.Empty).Trim(),
            StringComparison.OrdinalIgnoreCase);
}
