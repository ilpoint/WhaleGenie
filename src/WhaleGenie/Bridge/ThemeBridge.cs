using System;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Media;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

/// <summary>
/// The one theme choice, read and written through the same door the settings window uses, so a
/// custom accent cannot be applied without being stored or stored without being applied. The page
/// paints with what this says; it never reads a setting file of its own.
/// </summary>
public sealed class ThemeBridge : IBridgeHandler
{
    private BridgeServer? _server;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("theme.get", _ =>
        {
            server.DropPending("theme.changed");
            return Task.FromResult<object?>(Current());
        });
        server.Register("theme.set", SetAsync);
    }

    private static object Current()
    {
        var selection = ThemeController.Load();
        var requested = ThemePalette.TryParse(selection.AccentHex, out var accent)
            ? accent
            : Color.Parse(ThemePalette.DefaultAccent);
        var usable = selection.Mode is not ThemeMode.Custom
                     || ThemePalette.TryDerive(requested, selection.BaseLight, out _);
        if (!ThemePalette.TryDerive(requested, selection.BaseLight, out var tokens)
            && !ThemePalette.TryDerive(Color.Parse(ThemePalette.DefaultAccent), selection.BaseLight,
                out tokens))
        {
            tokens = default;
        }

        return new
        {
            mode = selection.Mode.ToString().ToLowerInvariant(),
            baseLight = selection.BaseLight,
            accentHex = selection.AccentHex,
            accentUsable = usable,
            fill = ThemePalette.ToHex(tokens.Fill),
            surface = ThemePalette.ToHex(tokens.Surface),
            text = ThemePalette.ToHex(tokens.Text),
            defaultAccent = ThemePalette.DefaultAccent,
        };
    }

    private async Task<object?> SetAsync(JsonElement parameters)
    {
        var mode = parameters.TryGetProperty("mode", out var named) && named.GetString() is { } text
            ? text.ToLowerInvariant() switch
            {
                "dark" => ThemeMode.Dark,
                "light" => ThemeMode.Light,
                "custom" => ThemeMode.Custom,
                _ => throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.ThemeMode")),
            }
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.ThemeNeedsMode"));

        bool? baseGiven = null;
        if (parameters.TryGetProperty("baseLight", out var baseElement)
            && baseElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            baseGiven = baseElement.GetBoolean();
        }

        var accentHex = parameters.TryGetProperty("accentHex", out var accent) && accent.ValueKind == JsonValueKind.String
            ? accent.GetString()
            : null;

        // WithMode pairs the built-in modes with their variant, the same pairing the settings
        // window makes: a choice that says light on a dark base would paint light colours on dark
        // controls.
        var selection = new ThemeSelection(mode, baseGiven ?? mode == ThemeMode.Light, accentHex).WithMode(mode);
        ThemeController.Apply(selection);
        ThemeController.Save(selection);

        await _server!.PushAsync("theme.changed", Current());
        return Current();
    }
}
