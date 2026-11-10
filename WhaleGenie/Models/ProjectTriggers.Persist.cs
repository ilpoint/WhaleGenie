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

// Reading and writing the whole set: the bindings allowed, the copy and the saved form.
public sealed partial class ProjectTriggers
{
    public static bool IsAllowedMenuLauncher(string? binding)
    {
        var text = binding?.Trim() ?? string.Empty;
        if (text.Length == 0
            || text.Contains("Wheel ", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse Left", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse Right", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (text.Equals("Mouse Middle", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse 4", StringComparison.OrdinalIgnoreCase)
            || text.Equals("Mouse 5", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !text.StartsWith("Mouse ", StringComparison.OrdinalIgnoreCase)
               && HotkeyBinding.IsSupported(text);
    }

    public static string GroupKey(WindowRule rule)
        => string.Join('\u001f',
            rule.Title, rule.Process, rule.ClassName, rule.How,
            rule.CaseSensitive, rule.Trim, rule.Multiline);

    public ProjectTriggers Copy()
    {
        var copy = new ProjectTriggers();
        foreach (var entry in Entries)
        {
            copy.Entries.Add(entry.Copy());
        }

        foreach (var layout in InterfaceLayouts)
        {
            copy.InterfaceLayouts.Add(layout with { });
        }

        copy.MenuPages.Clear();
        foreach (var page in MenuPages)
        {
            copy.MenuPages.Add(page with { });
        }
        foreach (var rule in BlockedMenuRules)
        {
            copy.BlockedMenuRules.Add(rule with { });
        }

        copy.Wheel = Wheel.Copy();
        return copy;
    }

    public JsonObject ToJson()
    {
        var entries = new JsonArray();
        foreach (var entry in Entries)
        {
            entries.Add(entry.ToJson());
        }

        var layouts = new JsonArray();
        foreach (var layout in InterfaceLayouts)
        {
            layouts.Add(layout.ToJson());
        }

        return new JsonObject
        {
            ["entries"] = entries,
            ["interfaceLayouts"] = layouts,
            ["menu"] = new JsonObject
            {
                ["pages"] = new JsonArray([.. MenuPages.Select(page => page.ToJson())]),
                ["blockedRules"] = new JsonArray(
                    [.. BlockedMenuRules.Select(rule => (JsonNode)rule.ToJson())]),
            },
            ["wheel"] = Wheel.ToJson(),
        };
    }

    public static ProjectTriggers FromJson(JsonObject? node)
    {
        var triggers = new ProjectTriggers();
        foreach (var item in node?["entries"] as JsonArray ?? [])
        {
            if (item is JsonObject entry)
            {
                triggers.Entries.Add(TriggerEntry.FromJson(entry));
            }
        }

        foreach (var item in node?["interfaceLayouts"] as JsonArray ?? [])
        {
            if (item is JsonObject layout)
            {
                triggers.InterfaceLayouts.Add(InterfaceLayout.FromJson(layout));
            }
        }

        if (node?["menu"] is JsonObject menu)
        {
            triggers.MenuPages.Clear();
            foreach (var item in menu["pages"] as JsonArray ?? [])
            {
                if (item is JsonObject page)
                {
                    triggers.MenuPages.Add(MenuPage.FromJson(page));
                }
            }
            foreach (var item in menu["blockedRules"] as JsonArray ?? [])
            {
                if (item is JsonObject rule)
                {
                    triggers.BlockedMenuRules.Add(WindowRule.FromJson(rule));
                }
            }
        }

        triggers.Wheel = node?["wheel"] is JsonObject wheel
            ? WheelMenuRules.FromJson(wheel)
            : new WheelMenuRules();
        triggers.EnsureMenuPages();
        return triggers;
    }

    public static ProjectTriggers FromMacros(IEnumerable<MacroItem> macros)
    {
        var triggers = new ProjectTriggers();
        foreach (var macro in macros)
        {
            var entries = macro.Triggers.Count > 0
                ? macro.Triggers.Select(entry => entry.Copy())
                : [TriggerEntry.FromMacro(macro)];
            foreach (var entry in entries)
            {
                entry.MacroName = macro.Name;
                triggers.Entries.Add(entry);
            }
        }

        foreach (var layout in macros.SelectMany(macro => macro.InterfaceLayouts)
                     .DistinctBy(layout => layout.GroupKey))
        {
            triggers.InterfaceLayouts.Add(layout with { });
        }
        return triggers;
    }
}
