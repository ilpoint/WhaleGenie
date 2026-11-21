using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>
/// The one settings surface: the general page, the menu launcher, window rules, the wheel and its
/// action manager all read and write the same C# view model. The page is a view; every choice is
/// clamped, validated and stored here before a snapshot goes back over the bridge.
/// </summary>
internal sealed partial class SettingsBridge : IBridgeHandler
{
    private readonly SettingsViewModel _settings;
    private readonly MainViewModel _project;
    private readonly Func<Window?> _owner;
    private readonly Action _changed;

    public SettingsBridge(MainViewModel project, Func<Window?> owner, Action? changed = null)
        : this(new SettingsViewModel(SharedDevices.Current.Driver, null, project,
            project.Workspace.CurrentPath), project, owner, changed)
    {
    }

    internal SettingsBridge(SettingsViewModel settings, MainViewModel project,
        Func<Window?> owner, Action? changed = null)
    {
        _settings = settings;
        _project = project;
        _owner = owner;
        _changed = changed ?? (() => { });
    }

    public void Register(BridgeServer server)
    {
        server.Register("settings.state", _ => Task.FromResult<object?>(State()));
        server.Register("settings.update", Update);
        server.Register("settings.openFolder", OpenFolder);
        RegisterMenu(server);
        RegisterWheel(server);
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
            systemHotkey = _settings.SystemHotkey,
            recordStopKey = _settings.RecordStopKey,
            systemHotkeyProblem = _settings.SystemHotkeyProblem,
            recordStopProblem = _settings.RecordStopProblem,
            storage = _settings.StorageRows.Select(row => new
            {
                name = row.Name,
                what = row.What,
                where = row.Where,
                folder = row.Folder,
            }),
            driverStatus = _settings.DriverStatus,
            driverReady = _settings.DriverReady,
            attachDriverAtStart = _settings.AttachDriverAtStart,
            runAsAdmin = _settings.RunAsAdmin,
            adminStatus = _settings.AdminStatus,
            windowWay = _settings.SelectedWindowWay.Way.ToString(),
            screenWay = _settings.SelectedScreenWay.Way.ToString(),
            windowWays = _settings.WindowWays.Select(way => new
            {
                value = way.Way.ToString(),
                label = way.ToString(),
            }),
            screenWays = _settings.ScreenWays.Select(way => new
            {
                value = way.Way.ToString(),
                label = way.ToString(),
            }),
            menu = MenuState(),
            wheel = WheelState(),
        };

    private Task<object?> OpenFolder(JsonElement parameters)
    {
        var folder = parameters.TryGetProperty("folder", out var named) ? named.GetString() : null;
        if (folder is not { Length: > 0 }
            || !_settings.StorageRows.Any(row => string.Equals(row.Folder, folder,
                StringComparison.OrdinalIgnoreCase))
            || !Directory.Exists(folder))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.StorageProblem"));
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder)
        {
            UseShellExecute = true,
        });
        return Task.FromResult<object?>(null);
    }

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
            case "attachDriverAtStart":
                _settings.AttachDriverAtStart = flag;
                break;
            case "runAsAdmin":
                _settings.RunAsAdmin = flag;
                break;
            case "windowWay":
                _settings.SelectedWindowWay = _settings.WindowWays.First(way =>
                    string.Equals(way.Way.ToString(), value, StringComparison.OrdinalIgnoreCase));
                break;
            case "screenWay":
                _settings.SelectedScreenWay = _settings.ScreenWays.First(way =>
                    string.Equals(way.Way.ToString(), value, StringComparison.OrdinalIgnoreCase));
                break;
            case "language":
                Strings.Current.Language = value == "zh-Hans" ? Language.Chinese : Language.English;
                break;
            case "systemHotkey":
                if (value.Length == 0)
                {
                    _settings.ClearSystemHotkey();
                }
                else if (!_settings.CaptureSystemHotkey(value))
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        _settings.SystemHotkeyProblem);
                }

                break;
            case "recordStopKey":
                if (value.Length > 0 && !_settings.CaptureRecordStop(value))
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        _settings.RecordStopProblem);
                }
                else if (value.Length == 0)
                {
                    _settings.RecordStopKey = string.Empty;
                }

                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }

        Changed();
        return Task.FromResult<object?>(State());
    }

    private void Changed() => _changed();

    private static string? Text(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name, bool fallback = false)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static int Count(JsonElement element, string name, int fallback = 0)
        => element.ValueKind == JsonValueKind.Object
           && element.TryGetProperty(name, out var value)
           && value.TryGetInt32(out var number)
            ? number
            : fallback;
}
