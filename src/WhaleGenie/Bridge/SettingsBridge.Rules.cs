using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Bridge;

// Window rules shared by the menu and the wheel: the open windows the page can choose from, the
// one desktop pick that still needs a native overlay, and the rule shape both editors send back.
internal sealed partial class SettingsBridge
{
    private static object RuleDto(WindowRule rule) => new
    {
        title = rule.Title,
        process = rule.Process,
        className = rule.ClassName,
        handle = rule.Handle,
        how = rule.How,
        caseSensitive = rule.CaseSensitive,
        trim = rule.Trim,
        multiline = rule.Multiline,
    };

    private static WindowRule ReadRule(JsonElement parameters)
    {
        var node = parameters.ValueKind == JsonValueKind.Object
                   && parameters.TryGetProperty("rule", out var wrapped)
            ? wrapped
            : parameters;
        if (node.ValueKind != JsonValueKind.Object)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.WindowRuleRequired"));
        }

        var how = Text(node, "how")?.Trim() ?? WindowHow.TitleContains;
        if (!WindowHow.Values.Contains(how, StringComparer.Ordinal))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.WindowRuleRequired"));
        }

        var rule = new WindowRule(
            Text(node, "title") ?? string.Empty,
            Text(node, "process") ?? string.Empty,
            Text(node, "className") ?? string.Empty,
            Number(node, "handle"),
            how,
            Flag(node, "caseSensitive"),
            Flag(node, "trim", true),
            Flag(node, "multiline"));
        if (!rule.HasTarget)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.WindowRuleRequired"));
        }

        return rule;
    }

    private static long Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null,
            out number)
            ? number
            : 0;
    }

    private object WindowPickerState()
    {
        IReadOnlyList<WindowEntry> entries;
        try
        {
            entries = WindowPickerViewModel.From(SharedDevices.Current.Windows);
        }
        catch (Exception)
        {
            entries = [];
        }

        var own = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        return new
        {
            windows = entries
                .Where(entry => !string.Equals(entry.Process, own, StringComparison.OrdinalIgnoreCase))
                .Select(entry => new
                {
                    handle = entry.Handle,
                    title = entry.Title,
                    process = entry.Process,
                    className = entry.Class,
                    detail = entry.Detail,
                    summary = entry.Summary,
                }),
            hows = WindowHow.Values.Select((value, index) => new
            {
                value,
                label = Strings.Get($"WindowRule.How.{value}", WindowHow.Labels[index]),
            }),
        };
    }

    private Task<object?> WindowPicker(JsonElement parameters)
        => Task.FromResult<object?>(WindowPickerState());

    private async Task<object?> PickWindow()
    {
        if (_owner() is not { } owner)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.SettingsNeedsWindow"));
        }

        using var aside = AppWindows.StepAside(owner);
        await AppWindows.SettleAsync();
        var window = await WindowPickWindow.PickAsync(SharedDevices.Current.Windows,
            SharedDevices.Current.Screen, SharedDevices.Current.Hook);
        if (window is null)
        {
            return null;
        }

        var process = string.Empty;
        var className = string.Empty;
        try
        {
            process = SharedDevices.Current.Windows.ProcessOf(window.Handle);
            className = SharedDevices.Current.Windows.ClassOf(window.Handle);
        }
        catch (Exception)
        {
            // A window that stopped existing between the click and the read still has its
            // title and handle; an empty process or class just means the rule uses the title.
        }

        return new
        {
            handle = window.Handle,
            title = window.Title,
            process,
            className,
            detail = $"{window.Size.Width}×{window.Size.Height}",
            summary = string.Join(" · ", new[] { process, className }
                .Where(part => part.Length > 0)
                .Append($"{window.Size.Width}×{window.Size.Height}")),
        };
    }
}
