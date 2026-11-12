using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;


namespace WhaleGenie.Mcp;

// The pages a client can read without calling a tool: the catalogue, the project and its macros.
internal sealed partial class McpSelfCheck
{
    /// <summary>
    /// The catalogue as a resource: a client that can cache it reads it once, and one that cannot
    /// falls back to the tools. Both say the same thing, which is why this only checks that the
    /// resource is there and reads back as the catalogue.
    /// </summary>
    private async Task ResourcesAsync(CancellationToken token)
    {
        try
        {
            var uris = (await client.ListResourcesAsync(cancellationToken: token)
                    .ConfigureAwait(false))
                .Select(resource => resource.Uri).ToList();
            var templates = (await client.ListResourceTemplatesAsync(cancellationToken: token)
                    .ConfigureAwait(false))
                .Select(template => template.UriTemplate).ToList();

            Add("resources.listed",
                uris.Count > 0 || templates.Count > 0 ? McpCheckResult.Pass : McpCheckResult.Missing,
                uris.Count + templates.Count == 0
                    ? "the server lists no resources"
                    : $"{uris.Count} resources, {templates.Count} templates: "
                        + string.Join(" / ", uris.Concat(templates)));

            if (!uris.Any(uri => uri.Contains("actions", StringComparison.Ordinal)))
            {
                Add("resources.read", McpCheckResult.Missing, "there is no catalogue resource to read");
                return;
            }

            var read = await client.ReadResourceAsync(
                uris.First(uri => uri.Contains("actions", StringComparison.Ordinal)),
                cancellationToken: token).ConfigureAwait(false);
            var text = string.Concat(read.Contents
                .OfType<ModelContextProtocol.Protocol.TextResourceContents>()
                .Select(content => content.Text));
            var total = Field(text, "total");
            Add("resources.read",
                total is { Length: > 0 } ? McpCheckResult.Pass : McpCheckResult.Missing,
                total is { Length: > 0 }
                    ? $"the catalogue reads back as a resource with {total} actions"
                    : text);
        }
        catch (Exception error)
        {
            // A client older than resources, or a server without them, is a fact to record rather
            // than a reason to stop measuring everything else.
            Add("resources.listed", McpCheckResult.Missing, Describe(error));
        }
    }

    /// <summary>
    /// The project page and one macro page — the two things a client keeps instead of asking for the
    /// macro list every turn. What is checked is that they are there, that the project page is about
    /// the revision the tool reports, and that a macro page names the macro it was asked for. A
    /// project that holds no macro is a skip, not a failure: a read-only check never wrote one.
    /// </summary>
    private async Task ProjectPagesAsync(CancellationToken token)
    {
        try
        {
            var project = await PageAsync("whalegenie://project", token).ConfigureAwait(false);
            var status = await InvokeAsync("whalegenie_project_status",
                new Dictionary<string, object?>(StringComparer.Ordinal), token).ConfigureAwait(false);

            var revision = Field(project, "revision");
            var now = Field(status.Text, "revision");
            var agreed = revision is { Length: > 0 } && revision == now;
            Add("resources.project",
                revision is { Length: > 0 }
                    ? agreed ? McpCheckResult.Pass : McpCheckResult.Fail
                    : McpCheckResult.Missing,
                revision is not { Length: > 0 }
                    ? Clip(project)
                    : agreed
                        ? $"the project page reads at revision {revision}, the same one status reports"
                        : $"the project page says revision {revision}, status says {now}");

            if (Names(project) is not { Count: > 0 } names)
            {
                Add("resources.macro", McpCheckResult.Skip,
                    "this project holds no macro, so there is no page to read");
                return;
            }

            var name = names[0];
            var macro = await PageAsync(McpResources.MacroUri(name), token).ConfigureAwait(false);
            var echoed = Field(macro, "name");
            Add("resources.macro",
                echoed == name ? McpCheckResult.Pass : McpCheckResult.Fail,
                echoed == name
                    ? $"\"{name}\" reads as its own page, {OutlineCount(macro)} steps in the outline"
                    : Clip(macro));
        }
        catch (Exception error)
        {
            Add("resources.project", McpCheckResult.Missing, Describe(error));
        }
    }

    private async Task<string> PageAsync(string uri, CancellationToken token)
    {
        var read = await client.ReadResourceAsync(uri, cancellationToken: token).ConfigureAwait(false);
        return string.Concat(read.Contents
            .OfType<TextResourceContents>()
            .Select(content => content.Text));
    }

    /// <summary>The macro names a project page carries, in the order it lists them.</summary>
    private static List<string> Names(string project)
        => Parse(project) is { } document
            && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("macros", out var macros)
            ? [.. macros.EnumerateArray()
                .Select(macro => macro.ValueKind == JsonValueKind.Object
                    && macro.TryGetProperty("name", out var name)
                    ? name.GetString() ?? string.Empty
                    : string.Empty)
                .Where(name => name.Length > 0)]
            : [];

    /// <summary>How many steps one macro page says the macro has.</summary>
    private static int OutlineCount(string macro)
        => Parse(macro) is { } document
            && document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("outline", out var outline)
            && outline.ValueKind == JsonValueKind.Array
            ? outline.GetArrayLength()
            : 0;
}
