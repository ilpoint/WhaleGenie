using System;
using System.Text.Json;
using System.Threading.Tasks;
using Lucide.Avalonia;
using WhaleGenie.Contracts;

namespace WhaleGenie.Bridge;

/// <summary>
/// Turns the Lucide name a project stores into the path data the page draws. Keeping this on the
/// C# side means one icon table (the Lucide package) serves the window and the page, and a name
/// that no longer exists simply draws nothing instead of a wrong glyph.
/// </summary>
internal sealed class IconBridge : IBridgeHandler
{
    public void Register(BridgeServer server)
        => server.Register(BridgeMethods.IconPath, Path);

    private static Task<object?> Path(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("name", out var given)
            || given.GetString() is not { Length: > 0 } name)
        {
            return Task.FromResult<object?>(null);
        }

        if (!Enum.TryParse<LucideIconKind>(name, ignoreCase: true, out var kind))
        {
            return Task.FromResult<object?>(null);
        }

        var path = LucideIconExtensions.GetGeometryData(kind);
        return Task.FromResult<object?>(path.Length == 0 ? null : new { path });
    }
}
