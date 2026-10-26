using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// A package is the macro file, so a package written with the old field names is refused rather
/// than read as if its parameters still meant something. There is no migration branch: the version
/// has to be the one this build writes.
/// </summary>
public class MacroPackageFormatTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(9)]
    public void A_package_from_an_older_format_is_refused(int version)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "old.wgmacro");
            MacroPackage.Save(path, [new MacroItem { Name = "old" }], [], "test");
            RewriteVersion(path, version);

            var error = Assert.Throws<InvalidDataException>(() => MacroPackage.Load(path));
            Assert.Contains(version.ToString(), error.Message, StringComparison.Ordinal);
            Assert.Contains(MacroPackage.FormatVersion.ToString(), error.Message,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Child_macros_and_their_public_origin_survive_the_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "children.wgmacro");
            var parent = new MacroItem
            {
                Name = "parent",
                Description = "Parent description",
            };
            var child = new MacroItem
            {
                Name = "child",
                Description = "Child description",
                Origin = new MacroOrigin("public-id", "Public child", 6),
            };
            child.Steps.Add(new MacroStep { Type = "control.log" });
            parent.SubMacros.Add(child);

            MacroPackage.Save(path, [parent], [], "test");
            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var loadedChild = Assert.Single(loaded.SubMacros);

            Assert.Equal("Parent description", loaded.Description);
            Assert.Equal("Child description", loadedChild.Description);
            Assert.Equal("public-id", loadedChild.Origin!.Id);
            Assert.Equal(6, loadedChild.Origin.Version);
            Assert.Equal("control.log", Assert.Single(loadedChild.Steps).Type);
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
            triggers.MenuLauncher = "Ctrl+Alt+M";
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
            Assert.Equal("Ctrl+Alt+M", loaded.Triggers.MenuLauncher);
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

    [Fact]
    public void A_menu_image_is_carried_in_the_package_assets()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var icon = Path.Combine(folder, "menu.png");
            File.WriteAllBytes(icon, [137, 80, 78, 71]);
            var macro = new MacroItem { Name = "menu" };
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Menu,
                MacroName = macro.Name,
                MenuPageId = triggers.CommonMenuPages()[0].Id,
                MenuRow = 0,
                MenuColumn = 0,
                MenuIcon = new MenuIconReference(MenuIconKind.Image, icon),
            });
            var path = Path.Combine(folder, "menu.wgmacro");

            MacroPackage.Save(path, [macro], triggers, [], "test");

            using (var archive = ZipFile.OpenRead(path))
            {
                Assert.Contains(archive.Entries,
                    entry => entry.FullName.StartsWith("assets/", StringComparison.Ordinal)
                        && entry.Name == "menu.png");
            }

            var loaded = MacroPackage.Load(path);
            var loadedIcon = loaded.Triggers.Entries.Single(entry =>
                entry.Kind == TriggerKind.Menu).MenuIcon;
            Assert.Equal(MenuIconKind.Image, loadedIcon?.Kind);
            Assert.True(File.Exists(loadedIcon!.Value));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_package_with_conflicting_menu_cells_is_not_written()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var macro = new MacroItem { Name = "menu" };
            var triggers = new ProjectTriggers();
            var page = triggers.CommonMenuPages()[0];
            for (var index = 0; index < 2; index++)
            {
                triggers.Entries.Add(new TriggerEntry
                {
                    Kind = TriggerKind.Menu,
                    MacroName = macro.Name,
                    MenuPageId = page.Id,
                    MenuRow = 0,
                    MenuColumn = 0,
                });
            }

            Assert.Throws<InvalidDataException>(() =>
                MacroPackage.Save(Path.Combine(folder, "bad.wgmacro"),
                    [macro], triggers, [], "test"));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_background_input_step_keeps_its_rule_through_save_load_and_conversion()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "input.wgmacro");
            var rule = new WindowRule(Title: "Game", Process: "game", ClassName: "GameClass",
                Handle: 42, How: WindowHow.Process);
            var macro = new MacroItem { Name = "input" };
            macro.Steps.Add(new MacroStep
            {
                Type = "input.keyPress",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = "inputMode",
                        Kind = ActionParameterKind.Choice,
                        Value = "background",
                    },
                    new StepParameter
                    {
                        Name = "window",
                        Kind = ActionParameterKind.WindowRule,
                        Value = rule.ToJson().ToJsonString(),
                    },
                    new StepParameter
                    {
                        Name = "key",
                        Kind = ActionParameterKind.Key,
                        Value = "F5",
                    },
                ],
            });

            MacroPackage.Save(path, [macro], [], "test");
            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var step = Assert.Single(loaded.Steps);
            var window = Assert.Single(step.Parameters, parameter => parameter.Name == "window");
            Assert.Equal(ActionParameterKind.WindowRule, window.Kind);
            Assert.True(WindowRule.TryParse(window.Value, out var parsed));
            Assert.Equal(rule, parsed);

            var executable = Assert.Single(new[] { step }.ToExecutable());
            var executableWindow = Assert.Single(executable.Parameters,
                parameter => parameter.Name == "window");
            Assert.True(WindowRule.TryParse(executableWindow.Text, out var executableRule));
            Assert.Equal(rule, executableRule);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// What a call passes is written as rows, so a saved package has to bring those rows back: the
    /// values, and the mark that says an empty one is meant rather than left out. Losing either
    /// would change what the call does without saying anything.
    /// </summary>
    [Fact]
    public void A_call_keeps_what_it_passes_through_a_saved_package()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "call.wgmacro");
            var call = new MacroStep
            {
                Type = "control.runMacro",
                Id = "aaaa",
                Parameters =
                [
                    new StepParameter
                    {
                        Name = FieldNames.Macro,
                        Kind = ActionParameterKind.Macro,
                        Value = "整理",
                    },
                    new StepParameter
                    {
                        Name = FieldNames.Arguments,
                        Kind = ActionParameterKind.Arguments,
                        Rows =
                        [
                            Row(("name", "次数"), ("value", "$n")),
                            Row(("name", "标签"), ("value", string.Empty), ("given", "true")),
                        ],
                    },
                ],
            };

            var macro = new MacroItem { Name = "主" };
            macro.Steps.Add(call);
            MacroPackage.Save(path, [macro], [], "test");

            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var again = Assert.Single(loaded.Steps);
            var rows = Assert.Single(again.Parameters,
                parameter => parameter.Name == FieldNames.Arguments).Rows;
            Assert.Equal(2, rows.Count);
            Assert.Equal("次数", rows[0].Text(FieldNames.Name));
            Assert.Equal("$n", rows[0].Text(FieldNames.Value));
            Assert.Equal("标签", rows[1].Text(FieldNames.Name));
            Assert.Equal(string.Empty, rows[1].Text(FieldNames.Value));
            Assert.Equal("true", rows[1].Text(FieldNames.Given));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// A record argument may say which fields it holds, and a package has to bring them back: the
    /// names are what `$record.field` is checked against, so losing them would make a checked macro
    /// an unchecked one after a save.
    /// </summary>
    [Fact]
    public void A_record_argument_keeps_the_fields_it_declares()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);

        try
        {
            var path = Path.Combine(folder, "record.wgmacro");
            var macro = new MacroItem { Name = "主" };
            macro.Parameters.Add(new MacroParameter
            {
                Name = "点",
                Type = ActionReturnType.Record,
                Required = true,
                Fields =
                [
                    new MacroField { Name = "x", Type = ActionReturnType.Number },
                    new MacroField { Name = "y", Type = ActionReturnType.Number },
                ],
            });
            macro.Results.Add(new MacroResult
            {
                Name = "框",
                Type = ActionReturnType.Record,
                Fields = [new MacroField { Name = "left", Type = ActionReturnType.Number }],
            });

            MacroPackage.Save(path, [macro], [], "test");

            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var argument = Assert.Single(loaded.Parameters);
            Assert.Equal(["x", "y"], argument.Fields.Select(field => field.Name));
            Assert.All(argument.Fields, field => Assert.Equal(ActionReturnType.Number, field.Type));

            var result = Assert.Single(loaded.Results);
            Assert.Equal(["left"], result.Fields.Select(field => field.Name));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>One row of a list parameter, written the way the editor writes it.</summary>
    private static StepParameterRow Row(params (string Column, string? Value)[] columns)
    {
        var row = new StepParameterRow();
        foreach (var (column, value) in columns)
        {
            if (value is not null)
            {
                row.Columns[column] = value;
            }
        }

        return row;
    }

    private static void RewriteVersion(string path, int version)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Update);
        var manifest = archive.GetEntry("manifest.json")
            ?? throw new InvalidOperationException("the package has no manifest.json");

        string text;
        using (var reader = new StreamReader(manifest.Open()))
        {
            text = reader.ReadToEnd();
        }

        manifest.Delete();
        var replacement = archive.CreateEntry("manifest.json");
        using var writer = new StreamWriter(replacement.Open());
        writer.Write(text.Replace($"\"version\": {MacroPackage.FormatVersion}",
            $"\"version\": {version}", StringComparison.Ordinal));
    }
}
