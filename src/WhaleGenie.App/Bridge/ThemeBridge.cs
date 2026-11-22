using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// The one theme choice, read and written through the same door the settings window uses. The UI
/// project owns the palette and applies the resources; this handler owns the wire shape.
/// </summary>
public sealed class ThemeBridge(IThemeCommands theme) : IBridgeHandler
{
    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("theme.get", _ =>
        {
            server.DropPending("theme.changed");
            return Task.FromResult<object?>(theme.Current());
        });
        server.Register("theme.set", SetAsync);
    }

    private async Task<object?> SetAsync(JsonElement parameters)
    {
        var mode = parameters.TryGetProperty("mode", out var named)
                   && named.GetString() is { Length: > 0 } text
            ? text
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.ThemeNeedsMode"));

        bool? baseGiven = null;
        if (parameters.TryGetProperty("baseLight", out var baseElement)
            && baseElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            baseGiven = baseElement.GetBoolean();
        }

        var accentHex = parameters.TryGetProperty("accentHex", out var accent)
                        && accent.ValueKind == JsonValueKind.String
            ? accent.GetString()
            : null;

        var state = theme.Save(mode, baseGiven, accentHex)
            ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.ThemeMode"));

        await _server!.PushAsync("theme.changed", state);
        return state;
    }
}
