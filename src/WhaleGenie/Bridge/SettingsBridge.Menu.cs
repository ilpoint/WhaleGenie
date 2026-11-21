using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

// The menu launcher and the window rules behind the lower menu region.
internal sealed partial class SettingsBridge
{
    private void RegisterMenu(BridgeServer server)
    {
        server.Register("settings.windows.list", WindowPicker);
        server.Register("settings.windows.pick", _ => PickWindow());
        server.Register("settings.menu.capture", CaptureMenuLauncher);
        server.Register("settings.menu.clear", _ => Task.FromResult<object?>(ClearMenuLauncher()));
        server.Register("settings.menu.rule.add", AddMenuRule);
        server.Register("settings.menu.rule.edit", EditMenuRule);
        server.Register("settings.menu.rule.remove", RemoveMenuRule);
        server.Register("settings.menu.rule.addPage", AddMenuPage);
        server.Register("settings.menu.rule.move", MoveMenuRule);
        server.Register("settings.menu.rule.moveTo", MoveMenuRuleTo);
    }

    private object MenuState() => new
    {
        launcher = _settings.MenuLauncher,
        display = _settings.MenuLauncherDisplay,
        status = _settings.MenuLauncherStatus,
        isMiddle = _settings.MenuLauncherMiddle,
        isCustom = _settings.MenuLauncherCustom,
        problem = _settings.MenuRuleProblem,
        rules = _settings.MenuRules.Select(row => new
        {
            key = row.GroupKey,
            summary = row.Summary,
            pageCountLabel = row.PageCountLabel,
            isDefault = row.IsDefault,
            isBlocked = row.IsBlocked,
            canAddPage = !row.IsBlocked && row.PageCount < ProjectTriggers.MaxCommonMenuPages,
            rule = RuleDto(row.Rule),
        }),
    };

    private object ClearMenuLauncher()
    {
        _settings.ClearMenuLauncher();
        Changed();
        return State();
    }

    private Task<object?> CaptureMenuLauncher(JsonElement parameters)
    {
        var binding = Text(parameters, "binding")?.Trim();
        if (binding is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.MenuLauncherInvalid"));
        }

        if (_settings.CaptureMenuLauncher(binding))
        {
            Changed();
        }

        return Task.FromResult<object?>(State());
    }

    private Task<object?> AddMenuRule(JsonElement parameters)
    {
        _settings.AddMenuRule(ReadRule(parameters));
        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> EditMenuRule(JsonElement parameters)
    {
        var row = FindMenuRule(parameters);
        if (row.IsDefault)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.WindowRuleRequired"));
        }

        _settings.UpdateMenuRule(row, ReadRule(parameters));
        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> RemoveMenuRule(JsonElement parameters)
    {
        var row = FindMenuRule(parameters);
        if (!row.IsDefault)
        {
            _settings.RemoveMenuRule(row);
            Changed();
        }

        return Task.FromResult<object?>(State());
    }

    private Task<object?> AddMenuPage(JsonElement parameters)
    {
        var row = FindMenuRule(parameters);
        _settings.AddMenuPage(row);
        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> MoveMenuRule(JsonElement parameters)
    {
        var row = FindMenuRule(parameters);
        var step = parameters.TryGetProperty("step", out var given) && given.TryGetInt32(out var value)
            ? value
            : 0;
        _settings.MoveMenuRule(row, step);
        Changed();
        return Task.FromResult<object?>(State());
    }

    private Task<object?> MoveMenuRuleTo(JsonElement parameters)
    {
        var row = FindMenuRule(parameters);
        var key = Text(parameters, "target");
        var target = _settings.MenuRules.FirstOrDefault(candidate =>
            string.Equals(candidate.GroupKey, key, StringComparison.Ordinal));
        if (target is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.WindowRuleRequired"));
        }

        _settings.MoveMenuRuleTo(row, target);
        Changed();
        return Task.FromResult<object?>(State());
    }

    private MenuRuleRow FindMenuRule(JsonElement parameters)
    {
        var key = Text(parameters, "key");
        var row = _settings.MenuRules.FirstOrDefault(candidate =>
            string.Equals(candidate.GroupKey, key, StringComparison.Ordinal));
        return row ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNeedsField"));
    }
}
