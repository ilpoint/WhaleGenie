using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>A compact action entry for the MCP list.</summary>
public sealed record McpActionSummary(
    string Key, string Name, string Category, string Description, bool Hidden);

/// <summary>One category of the catalogue and how many actions it holds.</summary>
public sealed record McpActionCategory(string Name, int Count);

/// <summary>
/// One page of a search, with the total it was cut from. Without the total a client cannot tell
/// "that is all of them" from "there are more", and it has to keep guessing at queries to find out.
/// </summary>
public sealed record McpActionPage(
    int Total, int Offset, int Limit, bool HasMore,
    IReadOnlyList<McpActionCategory> Categories, IReadOnlyList<McpActionSummary> Actions);

/// <summary>One described action, and whether the key exists at all.</summary>
public sealed record McpActionLookup(string Key, bool Found, McpActionDetails? Action);

/// <summary>One column a row of a list field may hold, and whether a row has to name it.</summary>
public sealed record McpActionColumn(string Name, string Role, bool Required);

/// <summary>One field of a record a result hands over.</summary>
public sealed record McpActionField(string Name, string Type);

/// <summary>
/// One parameter of an action, in the shape an external author needs to fill in. Record fields
/// and number facts are included because a shape alone does not say how to write the value;
/// <see cref="Conditional"/> is included because a field that is only required in some modes reads
/// as always required otherwise, and a client would fill in a window rule for a foreground click.
/// </summary>
public sealed record McpActionParameter(
    string Name, string Label, string Kind, bool Required, string Default, string Hint,
    string ReadMode, string Shape, IReadOnlyList<string> Options,
    IReadOnlyList<string> ChildKeys, IReadOnlyList<string> Fields,
    IReadOnlyList<McpActionColumn> Columns, string? Unit,
    string? EmptyMeans, decimal? Minimum, decimal? Maximum, bool Conditional, string MissingKey);

/// <summary>One result an action can hand back, and the fields of the record it holds.</summary>
public sealed record McpActionReturn(
    string Key, string Label, string Type, bool Primary, IReadOnlyList<McpActionField> Fields);

/// <summary>
/// The full description of one action. <see cref="SignatureSource"/> says whose declaration decides
/// the arguments and results: an action that calls another macro takes them from that macro, which
/// is why its own parameter list cannot name them.
/// </summary>
public sealed record McpActionDetails(
    string Key, string Name, string Category, string Description, bool Hidden,
    IReadOnlyList<McpActionParameter> Parameters, IReadOnlyList<McpActionReturn> Returns,
    string SignatureSource, int TimeoutMs);

/// <summary>
/// The action catalogue as the MCP tools show it. It is the same catalogue the editor uses; only
/// the shape is flattened so a tool result stays readable and does not carry icons or control
/// objects.
/// </summary>
public static class ActionKnowledge
{
    private const int Page = 25;

    /// <summary>How many actions one page may hold. A client may ask for more, but the answer has
    /// to stay a page rather than the whole catalogue.</summary>
    public const int LargestPage = 200;

    public static McpActionPage Search(string? query, string? category, bool includeHidden,
        int offset, int limit)
    {
        var words = (query ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries
            | StringSplitOptions.TrimEntries);
        var wanted = (category ?? string.Empty).Trim();
        var size = Math.Clamp(limit <= 0 ? Page : limit, 1, LargestPage);

        IEnumerable<ActionDefinition> matches = ActionCatalog.Definitions;
        if (!includeHidden)
        {
            matches = matches.Where(definition => !definition.Hidden);
        }

        if (wanted.Length > 0)
        {
            matches = matches.Where(definition => string.Equals(
                definition.Category.ToString(), wanted, StringComparison.OrdinalIgnoreCase));
        }

        if (words.Length > 0)
        {
            matches = matches.Where(definition => words.All(word =>
                Contains(definition.Key, word)
                || Contains(definition.LocalName, word)
                || Contains(definition.LocalDescription, word)
                || Contains(definition.Category.ToString(), word)));
        }

        var ordered = matches.OrderBy(definition => definition.Key, StringComparer.Ordinal).ToList();
        var from = Math.Clamp(offset, 0, ordered.Count);
        var page = ordered.Skip(from).Take(size)
            .Select(definition => new McpActionSummary(
                definition.Key,
                definition.LocalName,
                definition.Category.ToString(),
                definition.LocalDescription,
                definition.Hidden))
            .ToList();

        return new McpActionPage(
            ordered.Count,
            from,
            size,
            from + page.Count < ordered.Count,
            Categories(),
            page);
    }

    /// <summary>
    /// Every category and how many actions it holds, over the whole catalogue. It is sent with each
    /// page so a client can see the shape of what it is searching before it searches.
    /// </summary>
    public static IReadOnlyList<McpActionCategory> Categories()
        =>
        [
            .. ActionCatalog.Definitions
                .GroupBy(definition => definition.Category.ToString(), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new McpActionCategory(group.Key, group.Count())),
        ];

    public static McpActionDetails? Find(string key)
    {
        var definition = ActionCatalog.Find(key);
        return definition is null ? null : Describe(definition);
    }

    /// <summary>
    /// Several actions at once. One call that describes the three keys a macro needs costs a third
    /// of the round trips, and a key that is merely wrong comes back marked rather than sinking the
    /// whole answer.
    /// </summary>
    public static IReadOnlyList<McpActionLookup> FindMany(IEnumerable<string> keys)
        =>
        [
            .. keys
                .Select(key => (key ?? string.Empty).Trim())
                .Where(key => key.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .Select(key => Find(key) is { } action
                    ? new McpActionLookup(key, true, action)
                    : new McpActionLookup(key, false, null)),
        ];

    /// <summary>
    /// The keys closest to one that does not exist. A model that wrote "control.delai" is one letter
    /// from the answer; handing back the neighbours costs nothing and saves a search.
    /// </summary>
    public static IReadOnlyList<string> Suggest(string key)
    {
        var trimmed = (key ?? string.Empty).Trim();
        var tail = trimmed.LastIndexOf('.') is var dot and >= 0 ? trimmed[(dot + 1)..] : trimmed;
        if (tail.Length < 2)
        {
            return [];
        }

        return
        [
            .. ActionCatalog.Definitions
                .Where(definition => definition.Key.Contains(tail, StringComparison.OrdinalIgnoreCase))
                .Select(definition => definition.Key)
                .Take(5),
        ];
    }

    private static McpActionDetails Describe(ActionDefinition definition)
    {
        // The merged parameter the editor draws does not carry "required only in some modes"; the
        // engine's own catalogue does, and it is the same one the runner reads.
        var facts = ActionSemanticsCatalog.Find(definition.Key);

        return new(
            definition.Key,
            definition.LocalName,
            definition.Category.ToString(),
            definition.LocalDescription,
            definition.Hidden,
            [
                .. definition.Parameters.Select(parameter => new McpActionParameter(
                    parameter.Name,
                    parameter.LocalLabel,
                    parameter.Kind.ToString(),
                    parameter.Required,
                    parameter.LocalDefault,
                    parameter.LocalHint,
                    parameter.ReadMode.ToString(),
                    parameter.Shape.ToString(),
                    [.. parameter.Options],
                    [.. parameter.ChildKeys],
                    FieldsOf(parameter.Shape),
                    ColumnsOf(facts?.Parameter(parameter.Name)),
                    parameter.Shape == ActionValueShape.Number ? parameter.Unit.ToString() : null,
                    parameter.Shape == ActionValueShape.Number
                        ? parameter.EmptyMeans.ToString()
                        : null,
                    parameter.Shape == ActionValueShape.Number ? parameter.Minimum : null,
                    parameter.Shape == ActionValueShape.Number ? parameter.Maximum : null,
                    facts?.Parameter(parameter.Name)?.Conditional ?? false,
                    facts?.Parameter(parameter.Name)?.MissingKey ?? string.Empty)),
            ],
            [
                .. definition.Returns.Select(result => new McpActionReturn(
                    result.Key, result.Label, result.Type.ToString(), result.Primary,
                    [.. (facts?.Return(result.Key)?.Fields ?? [])
                        .Select(field => new McpActionField(field.Name, field.Type.ToString()))])),
            ],
            facts?.SignatureSource.ToString() ?? ActionSignatureSource.Own.ToString(),
            facts?.StepTimeoutMs ?? 0);
    }

    /// <summary>
    /// The columns a row of a list field holds. A client that guesses them writes a row the action
    /// skips in silence, which looks like the step ignoring what was written.
    /// </summary>
    private static IReadOnlyList<McpActionColumn> ColumnsOf(ActionParameterSemantics? facts)
        => facts is null
            ? []
            : [.. facts.Columns.Select(column =>
                new McpActionColumn(column.Name, column.Role.ToString(), column.Required))];

    /// <summary>
    /// The names written inside a record, in the order the file writes them. A window rule is read
    /// from the same serialiser the package uses, so this list cannot drift from the field shape.
    /// </summary>
    /// <summary>
    /// The names written inside a record of this shape, in the order the file writes them. Empty for
    /// a shape that is one value rather than several.
    /// </summary>
    internal static IReadOnlyList<string> FieldsOf(ActionValueShape shape) => shape switch
    {
        ActionValueShape.WindowRule => [.. WindowRule.Empty.ToJson().Select(field => field.Key)],
        ActionValueShape.Point => [FieldNames.X, FieldNames.Y],
        ActionValueShape.Rect => [FieldNames.Left, FieldNames.Top, FieldNames.Width, FieldNames.Height],
        _ => [],
    };

    private static bool Contains(string text, string word)
        => text.Contains(word, StringComparison.OrdinalIgnoreCase);
}
