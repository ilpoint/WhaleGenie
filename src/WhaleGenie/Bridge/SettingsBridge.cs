using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>
/// The settings the page owns for now: the switches and numbers on the general page. The native
/// settings window still owns hotkey capture, menu/wheel editors and the driver page, but these
/// values are read and written through one C# view model instead of a second settings store.
/// </summary>
internal sealed class SettingsBridge : IBridgeHandler
{
    private readonly SettingsViewModel _settings = new();

    public void Register(BridgeServer server)
    {
        server.Register("settings.state", _ => Task.FromResult<object?>(State()));
        server.Register("settings.update", Update);
    }

    private object State()
        => new
        {
            loadLastProjectAtStart = _settings.LoadLastProjectAtStart,
            systemAtStart = _settings.SystemAtStart,
            enabledFirst = _settings.EnabledFirst,
            ignoreOwnInput = _settings.IgnoreOwnInput,
            soundSystem = _settings.SoundSystem,
            soundMacro = _settings.SoundMacro,
            preloadOcr = _settings.PreloadOcr,
            inputRate = _settings.InputRate,
            frameRateFollow = _settings.FrameRateFollow,
            frameRate = _settings.FrameRate,
            frameRateMax = _settings.FastestRate,
            frameCache = _settings.FrameCache,
            frameCacheAge = _settings.FrameCacheAge,
            logCount = _settings.LogCount,
            language = Strings.Current.Language == Language.Chinese ? "zh-Hans" : "en",
            theme = _settings.SelectedTheme.Mode.ToString(),
        };

    private Task<object?> Update(JsonElement parameters)
    {
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        var flag = bool.TryParse(value, out var parsed) && parsed;
        var number = double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var parsedNumber)
            ? parsedNumber
            : 0;
        switch (field)
        {
            case "loadLastProjectAtStart":
                _settings.LoadLastProjectAtStart = flag;
                break;
            case "systemAtStart":
                _settings.SystemAtStart = flag;
                break;
            case "enabledFirst":
                _settings.EnabledFirst = flag;
                break;
            case "ignoreOwnInput":
                _settings.IgnoreOwnInput = flag;
                break;
            case "soundSystem":
                _settings.SoundSystem = flag;
                break;
            case "soundMacro":
                _settings.SoundMacro = flag;
                break;
            case "preloadOcr":
                _settings.PreloadOcr = flag;
                break;
            case "frameRateFollow":
                _settings.FrameRateFollow = flag;
                break;
            case "frameCache":
                _settings.FrameCache = flag;
                break;
            case "inputRate":
                _settings.InputRate = number;
                break;
            case "frameRate":
                _settings.FrameRate = number;
                break;
            case "frameCacheAge":
                _settings.FrameCacheAge = number;
                break;
            case "logCount":
                _settings.LogCount = number;
                break;
            case "language":
                Strings.Current.Language = value == "zh-Hans" ? Language.Chinese : Language.English;
                break;
            case "theme":
                _settings.SelectedTheme = _settings.ThemeOptions.First(option =>
                    string.Equals(option.Mode.ToString(), value, StringComparison.OrdinalIgnoreCase));
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }

        return Task.FromResult<object?>(State());
    }
}
