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
/// The action catalogue as resources rather than answers. A client that can cache a resource reads it
/// once and keeps it; the same catalogue asked for as a tool answer has to be asked for again at
/// every turn, and a long macro is written over many turns.
/// </summary>
/// <remarks>
/// The resources say what the tools say — both are read off the same catalogue — so a client may use
/// either. The tools stay because a client that cannot read resources needs them.
/// </remarks>
[McpServerResourceType]
public sealed class McpResources
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
}
