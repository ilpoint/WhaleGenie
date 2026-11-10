using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// Where the menu sits and what its pages and icons are made of.
/// <summary>Where one window-bound floating group sits inside its target client area.</summary>
public sealed record InterfaceLayout(WindowRule Window, int OffsetX, int OffsetY,
    string ToggleHotkey = "")
{
    public string GroupKey => ProjectTriggers.GroupKey(Window);

    public JsonObject ToJson() => new()
    {
        ["window"] = Window.ToJson(),
        ["x"] = OffsetX,
        ["y"] = OffsetY,
        ["toggleHotkey"] = ToggleHotkey,
    };

    public static InterfaceLayout FromJson(JsonObject node)
        => new(
            node["window"] is JsonObject window ? WindowRule.FromJson(window) : WindowRule.Empty,
            node["x"]?.GetValue<int>() ?? 16,
            node["y"]?.GetValue<int>() ?? 64,
            node["toggleHotkey"]?.GetValue<string>() ?? string.Empty);
}

public enum MenuPageRegion
{
    Common,
    Context,
}

public enum MenuIconKind
{
    Builtin,
    Image,
}

/// <summary>A built-in line icon name or an image stored beside the project.</summary>
public sealed record MenuIconReference(MenuIconKind Kind, string Value)
{
    public bool IsEmpty => Value.Trim().Length == 0;

    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind.ToString().ToLowerInvariant(),
        ["value"] = Value,
    };

    public static MenuIconReference FromJson(JsonObject node)
        => new(Enum.TryParse<MenuIconKind>(node["kind"]?.GetValue<string>(), true, out var kind)
                ? kind
                : MenuIconKind.Builtin,
            node["value"]?.GetValue<string>() ?? string.Empty);
}

/// <summary>
/// One common page or one lower context layout. Common pages are paged horizontally. Context
/// pages are tried in list order against the foreground window; the default context page is used
/// when none matches.
/// </summary>
public sealed record MenuPage(
    string Id,
    MenuPageRegion Region,
    WindowRule Window,
    bool IsDefault = false)
{
    public JsonObject ToJson()
    {
        var node = new JsonObject
        {
            ["id"] = Id,
            ["region"] = Region.ToString().ToLowerInvariant(),
            ["default"] = IsDefault,
        };
        if (Window.HasTarget)
        {
            node["window"] = Window.ToJson();
        }

        return node;
    }

    public static MenuPage FromJson(JsonObject node)
        => new(
            node["id"]?.GetValue<string>() is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N")[..10],
            Enum.TryParse<MenuPageRegion>(node["region"]?.GetValue<string>(), true, out var region)
                ? region
                : MenuPageRegion.Common,
            node["window"] is JsonObject window ? WindowRule.FromJson(window) : WindowRule.Empty,
            node["default"]?.GetValue<bool>() ?? false);
}

/// <summary>One automatic interface group: every entry has the same normalised window rule.</summary>
public sealed record InterfaceGroupView(
    string Key, WindowRule Rule, IReadOnlyList<TriggerEntry> Entries);

public sealed record MenuRuleGroupView(
    string Key, WindowRule Rule, IReadOnlyList<MenuPage> Pages);
