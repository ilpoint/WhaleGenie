using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Contracts;
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

    private SettingsState State() => new(
        _settings.LoadLastProjectAtStart,
        _settings.SystemAtStart,
        _settings.EnabledFirst,
        _settings.IgnoreOwnInput,
        _settings.SoundSystem,
        _settings.SoundMacro,
        _settings.PreloadOcr,
        _settings.InputRate,
        _settings.FrameRateFollow,
        _settings.FrameRate,
        _settings.FastestRate,
        _settings.FrameCache,
        _settings.FrameCacheAge,
        _settings.LogCount,
        Strings.Current.Language == Language.Chinese ? "zh-Hans" : "en",
        _settings.SystemHotkey,
        _settings.RecordStopKey,
        _settings.SystemHotkeyProblem,
        _settings.RecordStopProblem,
        [.. _settings.StorageRows.Select(row => new SettingsStorage(
            row.Name, row.What, row.Where, row.Folder))],
        _settings.DriverStatus,
        _settings.DriverReady,
        _settings.AttachDriverAtStart,
        _settings.RunAsAdmin,
        _settings.AdminStatus,
        _settings.SelectedWindowWay.Way.ToString(),
        _settings.SelectedScreenWay.Way.ToString(),
        [.. _settings.WindowWays.Select(way => new SettingsChoice(
            way.Way.ToString(), way.ToString()))],
        [.. _settings.ScreenWays.Select(way => new SettingsChoice(
            way.Way.ToString(), way.ToString()))],
        MenuState(),
        WheelState());

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

    /// <summary>
    /// The one settings writer. A patch carries only the fields the page changed; each one is
    /// validated, clamped and stored here before the new state goes back over the bridge.
    /// </summary>
    private Task<object?> Update(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("patch", out var node)
            || node.ValueKind != JsonValueKind.Object)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        var patch = node.Deserialize<SettingsPatch>(PatchOptions)
            ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));

        var touched = false;
        if (patch.LoadLastProjectAtStart is { } loadLast) { _settings.LoadLastProjectAtStart = loadLast; touched = true; }
        if (patch.SystemAtStart is { } systemAtStart) { _settings.SystemAtStart = systemAtStart; touched = true; }
        if (patch.EnabledFirst is { } enabledFirst) { _settings.EnabledFirst = enabledFirst; touched = true; }
        if (patch.IgnoreOwnInput is { } ignoreOwnInput) { _settings.IgnoreOwnInput = ignoreOwnInput; touched = true; }
        if (patch.SoundSystem is { } soundSystem) { _settings.SoundSystem = soundSystem; touched = true; }
        if (patch.SoundMacro is { } soundMacro) { _settings.SoundMacro = soundMacro; touched = true; }
        if (patch.PreloadOcr is { } preloadOcr) { _settings.PreloadOcr = preloadOcr; touched = true; }
        if (patch.FrameRateFollow is { } frameRateFollow) { _settings.FrameRateFollow = frameRateFollow; touched = true; }
        if (patch.FrameCache is { } frameCache) { _settings.FrameCache = frameCache; touched = true; }
        if (patch.AttachDriverAtStart is { } attachDriver) { _settings.AttachDriverAtStart = attachDriver; touched = true; }
        if (patch.RunAsAdmin is { } runAsAdmin) { _settings.RunAsAdmin = runAsAdmin; touched = true; }
        if (patch.InputRate is { } inputRate) { _settings.InputRate = inputRate; touched = true; }
        if (patch.FrameRate is { } frameRate) { _settings.FrameRate = frameRate; touched = true; }
        if (patch.FrameCacheAge is { } frameCacheAge) { _settings.FrameCacheAge = frameCacheAge; touched = true; }
        if (patch.LogCount is { } logCount) { _settings.LogCount = logCount; touched = true; }
        if (patch.WindowWay is { } windowWay)
        {
            _settings.SelectedWindowWay = _settings.WindowWays.First(way =>
                string.Equals(way.Way.ToString(), windowWay, StringComparison.OrdinalIgnoreCase));
            touched = true;
        }

        if (patch.ScreenWay is { } screenWay)
        {
            _settings.SelectedScreenWay = _settings.ScreenWays.First(way =>
                string.Equals(way.Way.ToString(), screenWay, StringComparison.OrdinalIgnoreCase));
            touched = true;
        }

        if (patch.Language is { } language)
        {
            Strings.Current.Language = language == "zh-Hans" ? Language.Chinese : Language.English;
            touched = true;
        }

        if (patch.SystemHotkey is { } systemHotkey)
        {
            if (systemHotkey.Length == 0)
            {
                _settings.ClearSystemHotkey();
            }
            else if (!_settings.CaptureSystemHotkey(systemHotkey))
            {
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    _settings.SystemHotkeyProblem);
            }

            touched = true;
        }

        if (patch.RecordStopKey is { } recordStopKey)
        {
            if (recordStopKey.Length > 0 && !_settings.CaptureRecordStop(recordStopKey))
            {
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    _settings.RecordStopProblem);
            }

            if (recordStopKey.Length == 0)
            {
                _settings.RecordStopKey = string.Empty;
            }

            touched = true;
        }

        if (!touched)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        Changed();
        return Task.FromResult<object?>(State());
    }

    /// <summary>The patch's own serialization: camelCase input, no tolerance for extra shapes.</summary>
    private static readonly JsonSerializerOptions PatchOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

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
