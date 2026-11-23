using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

// The wheel's look and behaviour, its scenes and the action manager. The page draws the wheel from
// the geometry and labels computed here, so the desktop wheel and the editor cannot disagree.
internal sealed partial class SettingsBridge
{
    private const double WheelEditorDiameter = 340;

    private void RegisterWheel(BridgeServer server)
    {
        server.Register("settings.wheel.update", UpdateWheel);
        server.Register("settings.wheel.launcher", SetWheelLauncher);
        server.Register("settings.wheel.enabled", SetWheelEnabled);
        server.Register("settings.wheel.enableSystem", _ => Task.FromResult<object?>(EnableSystem()));
        server.Register("settings.wheel.repeat", SetWheelRepeat);
        server.Register("settings.wheel.scene.select", SelectWheelScene);
        server.Register("settings.wheel.scene.add", AddWheelScene);
        server.Register("settings.wheel.scene.edit", EditWheelScene);
        server.Register("settings.wheel.scene.remove", RemoveWheelScene);
        server.Register("settings.wheel.scene.move", MoveWheelScene);
        server.Register("settings.wheel.slot.set", SetWheelSlot);
    }

    private SettingsWheelState WheelState()
    {
        var scene = _settings.EditingWheelScene;
        var global = _settings.WheelGlobal;
        var metrics = WheelGeometry.For(WheelEditorDiameter, _settings.WideOuter,
            _settings.SelectedThirdCapacity.Value);
        return new SettingsWheelState(
            _settings.WheelEnabled,
            _settings.WheelLauncher,
            _settings.WheelLauncherStatus,
            _settings.WheelLauncherMessage,
            _settings.OnlyInPrograms,
            _settings.WheelDiameter,
            _settings.WheelFontSize,
            _settings.SelectedThirdCapacity.Value,
            _settings.WheelTimeoutMs,
            _settings.WheelRepeatKey,
            _settings.WheelRepeatKey.Length == 0
                ? Strings.Get("Common.None")
                : _settings.WheelRepeatKey,
            _settings.WideOuter,
            _settings.HideEmptyOuter,
            _settings.HideWheelLabelWithIcon,
            _settings.CenterName,
            _settings.ClampToScreen,
            _settings.MovePointerToCenter,
            _settings.ShowGuide,
            _settings.ExtendedHint,
            _settings.FollowTheme,
            _settings.Gloss,
            _settings.HoverBorder,
            _settings.PopAnimation,
            _settings.PopAnimationMs,
            _settings.IsSystemOff,
            _settings.WheelProblem,
            [.. _settings.WheelLauncherChoices.Select(choice => new SettingsWheelChoice(
                choice.Binding, Strings.Get(choice.Key)))],
            [.. _settings.ThirdCapacityOptions.Select(choice => new SettingsWheelChoice(
                choice.Value.ToString(CultureInfo.InvariantCulture), Strings.Get(choice.Key)))],
            [.. _settings.WheelScenes.Select(row => new SettingsWheelScene(
                row.Key, row.Summary, row.IsGlobal, RuleDto(row.Scene.Window)))],
            _settings.SelectedWheelScene?.Key ?? WheelScene.GlobalKey,
            new SettingsWheelGeometry(
                WheelEditorDiameter,
                metrics.Center,
                metrics.Inner,
                metrics.Outer,
                metrics.Third,
                metrics.Edge,
                metrics.OuterCount,
                metrics.ThirdCount,
                metrics.HasOuter,
                metrics.HasThird),
            scene is null ? Array.Empty<SettingsWheelSlot>() : WheelSlots(scene, global, true),
            WheelSlots(global, global, false),
            [.. _project.Macros.Select(macro => new SettingsMacroChoice(
                macro.Name, macro.IsEnabled))]);
    }

    private IReadOnlyList<SettingsWheelSlot> WheelSlots(
        WheelScene scene, WheelScene global, bool inherit)
    {
        var metrics = WheelGeometry.For(WheelEditorDiameter, _settings.WideOuter,
            _settings.SelectedThirdCapacity.Value);
        var slots = new List<SettingsWheelSlot>();
        foreach (var ring in new[]
                 {
                     WheelRing.Inner, WheelRing.Outer, WheelRing.Third, WheelRing.Extended,
                 })
        {
            var count = WheelShape.CountOf(ring, _settings.WideOuter,
                _settings.SelectedThirdCapacity.Value);
            for (var index = 0; index < count; index++)
            {
                var reference = new WheelSlotRef(ring, index);
                var own = scene.At(reference);
                var inherited = inherit && !scene.IsGlobal && own.IsEmpty
                                && !global.At(reference).IsEmpty;
                var effective = inherited ? global.At(reference) : own;
                var (x, y) = WheelGeometry.LabelAt(reference, metrics, WheelEditorDiameter);
                var ringLabel = Strings.Get(ring switch
                {
                    WheelRing.Inner => "Wheel.RingInner",
                    WheelRing.Outer => "Wheel.RingOuter",
                    WheelRing.Third => "Wheel.RingThird",
                    _ => "Wheel.RingExtended",
                });
                slots.Add(new SettingsWheelSlot(
                    ring.ToString().ToLowerInvariant(),
                    index,
                    x,
                    y,
                    Strings.Format("Wheel.ActionPosition", ringLabel, index + 1),
                    effective.Macro,
                    effective.Note,
                    effective.Label,
                    IconDto(effective.Icon),
                    inherited,
                    !own.IsEmpty,
                    effective.IsEmpty,
                    ring == WheelRing.Extended ? "extended"
                        : inherited ? "global"
                        : !effective.IsEmpty ? (scene.IsGlobal ? "global" : "override")
                        : "empty"));
            }
        }

        return slots;
    }

    private static SettingsWheelIcon? IconDto(MenuIconReference? icon)
        => icon is { IsEmpty: false }
            ? new SettingsWheelIcon(icon.Kind.ToString().ToLowerInvariant(), icon.Value)
            : null;

    private Task<object?> UpdateWheel(JsonElement parameters)
    {
        var field = Text(parameters, "field");
        var raw = Text(parameters, "value") ?? string.Empty;
        if (field is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        var flag = bool.TryParse(raw, out var parsedFlag) && parsedFlag;
        var number = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var parsedNumber)
            ? parsedNumber
            : 0;
        switch (field)
        {
            case "enabled":
                _settings.WheelEnabled = flag;
                break;
            case "onlyInPrograms":
                _settings.OnlyInPrograms = flag;
                break;
            case "diameter":
                _settings.WheelDiameter = number;
                break;
            case "fontSize":
                _settings.WheelFontSize = number;
                break;
            case "thirdCapacity":
                _settings.SelectedThirdCapacity = _settings.ThirdCapacityOptions.First(option =>
                    option.Value == (int)Math.Round(number));
                break;
            case "timeoutMs":
                _settings.WheelTimeoutMs = number;
                break;
            case "wideOuter":
                _settings.WideOuter = flag;
                break;
            case "hideEmptyOuter":
                _settings.HideEmptyOuter = flag;
                break;
            case "hideLabelWithIcon":
                _settings.HideWheelLabelWithIcon = flag;
                break;
            case "centerName":
                _settings.CenterName = flag;
                break;
            case "clampToScreen":
                _settings.ClampToScreen = flag;
                break;
            case "movePointerToCenter":
                _settings.MovePointerToCenter = flag;
                break;
            case "showGuide":
                _settings.ShowGuide = flag;
                break;
            case "extendedHint":
                _settings.ExtendedHint = flag;
                break;
            case "followTheme":
                _settings.FollowTheme = flag;
                break;
            case "gloss":
                _settings.Gloss = flag;
                break;
            case "hoverBorder":
                _settings.HoverBorder = flag;
                break;
            case "popAnimation":
                _settings.PopAnimation = flag;
                break;
            case "popAnimationMs":
                _settings.PopAnimationMs = (int)Math.Round(number);
                break;
            case "launcher":
                if (raw.Length == 0)
                {
                    _settings.ClearWheelLauncher();
                }
                else
                {
                    _settings.SetWheelLauncher(raw);
                }

                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }

        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> SetWheelLauncher(JsonElement parameters)
    {
        var value = Text(parameters, "value") ?? string.Empty;
        if (value.Length == 0)
        {
            _settings.ClearWheelLauncher();
        }
        else
        {
            _settings.SetWheelLauncher(value);
        }

        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> SetWheelEnabled(JsonElement parameters)
    {
        _settings.WheelEnabled = parameters.TryGetProperty("value", out var given)
                                 && given.ValueKind == JsonValueKind.True;
        Changed();
        return Task.FromResult<object?>(State());
    }

    private object EnableSystem()
    {
        _settings.EnableSystem();
        Changed();
        return State();
    }

    private Task<object?> SetWheelRepeat(JsonElement parameters)
    {
        var binding = Text(parameters, "binding")?.Trim();
        if (binding is not { Length: > 0 }
            || !_settings.CaptureWheelRepeat(binding))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.HotkeyUnsupported"));
        }

        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> SelectWheelScene(JsonElement parameters)
    {
        var key = Text(parameters, "key");
        var row = _settings.WheelScenes.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        if (row is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        _settings.SelectedWheelScene = row;
        return Task.FromResult<object?>(State());
    }

    private Task<object?> AddWheelScene(JsonElement parameters)
    {
        _settings.AddWheelScene(ReadRule(parameters));
        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> EditWheelScene(JsonElement parameters)
    {
        var row = FindWheelScene(parameters);
        if (row.IsGlobal)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        _settings.UpdateWheelScene(row, ReadRule(parameters));
        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> RemoveWheelScene(JsonElement parameters)
    {
        var row = FindWheelScene(parameters);
        _settings.RemoveWheelScene(row);
        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> MoveWheelScene(JsonElement parameters)
    {
        _settings.MoveWheelScene(parameters.TryGetProperty("step", out var given)
                                 && given.TryGetInt32(out var step)
            ? step
            : 0);
        Changed();
        return Task.FromResult<object?>(State());
    }

    private WheelSceneRow FindWheelScene(JsonElement parameters)
    {
        var key = Text(parameters, "key") ?? _settings.SelectedWheelScene?.Key;
        var row = _settings.WheelScenes.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, key, StringComparison.Ordinal));
        return row ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNeedsField"));
    }

    private Task<object?> SetWheelSlot(JsonElement parameters)
    {
        var ringName = Text(parameters, "ring");
        if (!Enum.TryParse<WheelRing>(ringName, ignoreCase: true, out var ring))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        var index = parameters.TryGetProperty("index", out var given) && given.TryGetInt32(out var at)
            ? Math.Max(0, at)
            : 0;
        var reference = new WheelSlotRef(ring, index);
        var current = _settings.WheelSlotForEdit(reference);
        var icon = ReadIcon(parameters);
        if (icon is { Kind: MenuIconKind.Image, IsEmpty: false }
            && (current?.Icon is not { Kind: MenuIconKind.Image } previous
                || !string.Equals(previous.Value, icon.Value, StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                icon = new MenuIconReference(icon.Kind,
                    ImageAssets.Adopt(icon.Value, _project.CurrentPath));
            }
            catch (Exception)
            {
                // A picture that cannot be copied stays where it was picked from; the wheel can
                // still draw it while the original file is there, and the page is not blocked.
            }
        }

        _settings.SetWheelAction(reference, new WheelSlot
        {
            Macro = Text(parameters, "macro") ?? string.Empty,
            Note = Text(parameters, "note") ?? string.Empty,
            Icon = icon,
        });
        Changed();
        return Task.FromResult<object?>(State());
    }

    private static MenuIconReference? ReadIcon(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("icon", out var icon)
            || icon.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (icon.ValueKind != JsonValueKind.Object)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        var kind = Text(icon, "kind");
        if (!Enum.TryParse<MenuIconKind>(kind, ignoreCase: true, out var parsed))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        var value = Text(icon, "value") ?? string.Empty;
        return value.Trim().Length == 0
            ? null
            : new MenuIconReference(parsed, value);
    }

}
