using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Domain.Tests;

// What a macro keeps: its input limit, its switch key, its triggers and its window rule.
public partial class MacroPackageFormatTests
{
    /// <summary>
    /// 每宏限速与启用/停用键随宏一起走。没改过的两格不写进文件：缺省就是「跟随程序设置」与
    /// 「没有切换键」，所以同一个格式版本还是读得开旧包。
    /// </summary>
    [Fact]
    public void A_macro_keeps_its_own_input_limit_and_switch_key()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "own.wgmacro");
            var own = new MacroItem { Name = "own", InputRate = 300, ToggleKey = "F9" };
            var plain = new MacroItem { Name = "plain" };

            MacroPackage.Save(path, [own, plain], [], "test");
            var loaded = MacroPackage.Load(path).Macros;

            var readBack = loaded.Single(macro => macro.Name == "own");
            Assert.Equal(300, readBack.InputRate);
            Assert.Equal("F9", readBack.ToggleKey);

            var untouched = loaded.Single(macro => macro.Name == "plain");
            Assert.Null(untouched.InputRate);
            Assert.Equal(string.Empty, untouched.ToggleKey);

            // 文件形状没有为新增加宽：没写过的格子照旧不出现。
            var node = plain.ToJson();
            Assert.False(node.ContainsKey("inputRate"));
            Assert.False(node.ContainsKey("toggleKey"));
            Assert.Null(MacroItem.FromJson(node).InputRate);
            Assert.Equal(string.Empty, MacroItem.FromJson(node).ToggleKey);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Trigger_entries_and_surface_layouts_live_in_triggers_json()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "multi.wgmacro");
            var macro = new MacroItem { Name = "multi" };
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Key,
                MacroName = macro.Name,
                BindKey = "F8",
            });
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Hotstring,
                MacroName = macro.Name,
                Abbreviation = ";go",
                HotstringEndChars = "\n",
            });
            var page = triggers.AddContextMenuPage(new WindowRule(Title: "Notepad"));
            triggers.AddContextMenuPage(new WindowRule(Title: "Notepad"));
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Menu,
                MacroName = macro.Name,
                MenuPageId = page.Id,
                MenuRow = 1,
                MenuColumn = 2,
            });
            var blocked = new WindowRule(Process: "game", How: WindowHow.ProcessEquals);
            triggers.BlockedMenuRules.Add(blocked);

            MacroPackage.Save(path, [macro], triggers, [], "test");
            string triggerJson;
            using (var archive = ZipFile.OpenRead(path))
            {
                Assert.NotNull(archive.GetEntry("triggers.json"));
                using var reader = new StreamReader(
                    archive.GetEntry("triggers.json")!.Open());
                triggerJson = reader.ReadToEnd();
            }
            Assert.DoesNotContain("\"label\"", triggerJson, StringComparison.Ordinal);
            Assert.Contains("\"blockedRules\"", triggerJson, StringComparison.Ordinal);

            var loaded = MacroPackage.Load(path);

            Assert.Equal(3, loaded.Triggers.Entries.Count);
            Assert.Equal(3, Assert.Single(loaded.Macros).Triggers.Count);
            Assert.Contains(loaded.Triggers.Entries,
                entry => entry.Kind == TriggerKind.Hotstring && entry.Abbreviation == ";go");
            Assert.Equal("\n", loaded.Triggers.Entries
                .Single(entry => entry.Kind == TriggerKind.Hotstring).HotstringEndChars);
            Assert.Equal(blocked, Assert.Single(loaded.Triggers.BlockedMenuRules));
            Assert.Single(loaded.Triggers.CommonMenuPages());
            Assert.Equal(2, loaded.Triggers.ContextMenuPages().Count);
            var menu = loaded.Triggers.Entries.Single(entry => entry.Kind == TriggerKind.Menu);
            Assert.Equal((1, 2), (menu.MenuRow, menu.MenuColumn));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_captured_window_rule_survives_the_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "rule.wgmacro");
            var rule = new WhaleGenie.Core.Devices.WindowRule(
                Title: "Game",
                Process: "game",
                ClassName: "GameClass",
                Handle: 42,
                How: WhaleGenie.Core.Devices.WindowHow.Process,
                CaseSensitive: true,
                Trim: false,
                Multiline: true);
            var macro = new MacroItem
            {
                Name = "rule",
                TriggerMode = MacroTrigger.Window,
                WindowTarget = rule,
                WindowChange = WindowChangeKind.Disappeared,
            };

            MacroPackage.Save(path, [macro], [], "test");
            var loaded = Assert.Single(MacroPackage.Load(path).Macros);

            Assert.Equal(rule, loaded.WindowTarget);
            Assert.Equal(WindowChangeKind.Disappeared, loaded.WindowChange);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
