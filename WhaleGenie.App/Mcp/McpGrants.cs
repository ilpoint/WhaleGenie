using WhaleGenie.Localization;

namespace WhaleGenie.Mcp;

/// <summary>
/// The two permissions as the MCP flyout last left them, read back from the settings file. A server
/// runs in its own process, launched by a client that never asks again, so the file is the only
/// place the page and the server can meet: the page writes it, the server reads it whenever a call
/// needs a grant.
/// </summary>
/// <remarks>
/// Read on every question rather than remembered, which is what makes a switch take effect on the
/// next call instead of at the next start. The file is a few hundred bytes and the question is asked
/// at the pace a person or a model asks it, so the read is not worth caching — and a cache is one
/// more thing that can answer with yesterday's yes. A read that lands in the middle of the page
/// writing the file comes back as "no grant", which refuses a call rather than allowing one.
/// </remarks>
internal static class McpGrants
{
    /// <summary>The permissions in force, as the file has them right now.</summary>
    internal static (bool Control, bool Run) Current() => LocalSettings.LoadMcpGrants();
}
