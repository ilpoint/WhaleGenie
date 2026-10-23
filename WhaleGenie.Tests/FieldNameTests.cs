using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// A field name is part of the macro file, so the same name may not mean two things. The editor
/// facts are checked against the engine facts under the same name, and names whose labels or
/// choices deliberately vary from one action to another are listed in the developer notes with a
/// reason.
/// </summary>
public class FieldNameTests
{
    [Fact]
    public void One_name_is_one_shape_and_one_editor()
    {
        var whitelist = Whitelist();
        var problems = new List<string>();

        Ui.Run(() =>
        {
            var fields = ActionCatalog.Definitions
                .SelectMany(action => action.Parameters.Select(parameter => (Action: action.Key, Parameter: parameter)))
                .ToList();

            foreach (var group in fields.GroupBy(field => field.Parameter.Name, StringComparer.Ordinal))
            {
                var forms = group
                    .Select(field => (field.Parameter.Kind, field.Parameter.Shape, field.Parameter.ReadMode))
                    .Distinct()
                    .ToList();
                if (forms.Count > 1)
                {
                    problems.Add($"{group.Key} is drawn or read in {forms.Count} ways: "
                        + string.Join(", ", forms.Select(form => $"{form.Kind}/{form.Shape}/{form.ReadMode}")));
                }

                var variations = group
                    .Select(field => string.Join("|", field.Parameter.Label, field.Parameter.OptionLabels.Count,
                        string.Join(",", field.Parameter.Options), string.Join(",", field.Parameter.OptionLabels)))
                    .Distinct(StringComparer.Ordinal)
                    .Count();
                if (variations > 1 && !whitelist.ContainsKey(group.Key))
                {
                    problems.Add($"{group.Key} has labels or choices of its own and is not in the retained-name table");
                }
            }

            foreach (var name in whitelist.Keys)
            {
                if (!fields.Any(field => string.Equals(field.Parameter.Name, name, StringComparison.Ordinal)))
                {
                    problems.Add($"the retained-name table holds {name}, which no action uses");
                }
            }
        });

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void The_macro_file_has_one_name_for_each_top_level_thing()
    {
        var macro = new MacroItem
        {
            Name = "one",
            ProcessName = "notepad",
            RecordMouseMovement = false,
        };
        macro.Parameters.Add(new MacroParameter { Name = "count", Description = "How many." });
        macro.Results.Add(new MacroResult { Name = "answer", Description = "What was left." });

        var json = macro.ToJson();

        Assert.True(json.ContainsKey("process"));
        Assert.True(json.ContainsKey("recorder"));
        Assert.False(json.ContainsKey("program"));
        Assert.False(json.ContainsKey("record"));
        Assert.False(json.ContainsKey("trigger"));
        Assert.False(json.ContainsKey("loop"));

        var arguments = Assert.IsType<JsonArray>(json["arguments"]);
        var argument = Assert.IsType<JsonObject>(Assert.Single(arguments));
        Assert.True(argument.ContainsKey("description"));
        Assert.False(argument.ContainsKey("hint"));

        var results = Assert.IsType<JsonArray>(json["returns"]);
        var result = Assert.IsType<JsonObject>(Assert.Single(results));
        Assert.True(result.ContainsKey("description"));
        Assert.False(result.ContainsKey("hint"));
    }

    [Fact]
    public void Old_top_level_names_are_not_read_back()
    {
        var macro = MacroItem.FromJson(new JsonObject
        {
            ["name"] = "old",
            ["program"] = new JsonObject { ["name"] = "notepad" },
            ["record"] = new JsonObject { ["mouseMovement"] = false },
            ["trigger"] = new JsonObject { ["kind"] = "old" },
            ["loop"] = new JsonObject { ["kind"] = "old" },
        });

        Assert.Equal(string.Empty, macro.ProcessName);
        Assert.True(macro.RecordMouseMovement);

        var meta = StepMeta.FromJson(new JsonObject { ["retry"] = 4 });
        Assert.Equal(0, meta.RetryCount);
        Assert.True(meta.ToJson().ContainsKey("retryCount") is false);

        meta = new StepMeta { RetryCount = 4 };
        Assert.True(meta.ToJson().ContainsKey("retryCount"));
        Assert.False(meta.ToJson().ContainsKey("retry"));
    }

    [Fact]
    public void The_row_and_area_names_are_constants()
    {
        string[] names =
        [
            FieldNames.Key, FieldNames.Name, FieldNames.Left, FieldNames.Top,
            FieldNames.Width, FieldNames.Height,
        ];

        Assert.Equal(6, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["key", "name", "left", "top", "width", "height"], names);
    }

    [Fact]
    public void An_old_action_key_is_not_aliased()
    {
        Ui.Run(() => Assert.Null(ActionCatalog.Find("condition.variableEquals")));
    }

    /// <summary>
    /// A field name is the file format, so a name that was replaced in one action may not quietly
    /// come back there as an alias or as a second spelling of the same question.
    /// </summary>
    [Fact]
    public void A_retired_field_name_does_not_come_back()
    {
        (string Action, string Old)[] retired =
        [
            ("process.start", "arguments"), ("command.run", "arguments"),
            ("script.run", "arguments"), ("script.python", "arguments"),
            ("script.csharp", "arguments"), ("script.ahk", "arguments"),
            ("browser.upload", "files"),
            ("control.for", "from"), ("control.for", "to"), ("control.for", "step"),
            ("clipboard.writeImage", "image"),
            ("input.keySequence", "keys"),
            ("control.sequence", "name"),
            ("file.path", "name"),
            ("process.waitFor", "name"), ("process.exists", "name"),
            ("system.environment", "name"),
            ("input.mouseDrag", "steps"),
            ("process.info", "target"), ("process.kill", "target"),
            ("control.note", "text"),
            ("control.for", "variable"),
            ("browser.click", "how"), ("browser.fill", "how"),
            ("browser.waitForUrl", "how"), ("browser.scroll", "how"),
            ("browser.dialog", "how"), ("browser.switchTab", "how"),
            ("gamepad.button", "mode"),
            ("file.writeText", "mode"), ("file.writeCsv", "mode"),
            ("uia.check", "state"), ("uia.expand", "state"),
            ("browser.waitFor", "state"), ("browser.waitForLoad", "state"),
            ("browser.switchTab", "index"),
            ("input.keySequence", "intervalMs"), ("browser.fill", "intervalMs"),
            ("data.hash", "algorithm"),
            ("vision.findImage", "algorithm"), ("vision.waitImage", "algorithm"),
            ("vision.clickImage", "algorithm"),
            ("data.hash", "source"), ("script.run", "source"),
            ("process.start", "file"), ("command.run", "file"), ("script.run", "file"),
            ("system.volume", "percent"), ("system.brightness", "percent"),
            ("system.power", "what"), ("system.volume", "what"), ("system.sound", "what"),
            ("system.notify", "what"), ("system.ime", "what"),
            ("system.brightness", "what"),
            ("browser.readText", "what"), ("browser.readAll", "what"),
            ("control.switch", "matchMode"), ("file.readCsv", "matchMode"),
            ("uia.exists", "selector"), ("uia.find", "selector"),
            ("uia.waitElement", "selector"), ("uia.click", "selector"),
            ("uia.invoke", "selector"), ("uia.setText", "selector"),
            ("uia.read", "selector"), ("uia.select", "selector"),
            ("uia.check", "selector"), ("uia.expand", "selector"),
            ("uia.scrollIntoView", "selector"), ("uia.readTable", "selector"),
            ("uia.focusWindow", "selector"),
            ("browser.click", "target"), ("browser.fill", "target"),
            ("browser.readText", "target"), ("browser.readAll", "target"),
            ("browser.readTable", "target"), ("browser.select", "target"),
            ("browser.check", "target"), ("browser.hover", "target"),
            ("browser.press", "target"), ("browser.waitFor", "target"),
            ("browser.scroll", "target"), ("browser.download", "target"),
            ("browser.upload", "target"),
            ("vision.findImage", "image"), ("vision.waitImage", "image"),
            ("vision.clickImage", "image"),
            ("browser.press", "key"),
            ("clipboard.copy", "keys"), ("clipboard.paste", "keys"),
            ("system.notify", "message"),
            ("file.writeText", "text"), ("file.appendLog", "text"),
            ("data.base64Encode", "text"), ("data.base64Decode", "text"),
            ("data.hash", "text"), ("clipboard.writeText", "text"),
            ("clipboard.paste", "text"), ("input.typeText", "text"),
            ("uia.setText", "text"), ("browser.fill", "text"),
            ("browser.dialog", "text"),
            ("ocr.findText", "text"), ("ocr.clickText", "text"),
            ("file.writeJson", "value"), ("browser.select", "value"),
            ("control.switch", "value"),
            ("control.listInsert", "index"), ("control.listRemoveAt", "index"),
            ("control.listSet", "index"),
            ("browser.waitForUrl", "match"), ("browser.switchTab", "match"),
        ];

        var problems = new List<string>();
        Ui.Run(() =>
        {
            foreach (var (key, old) in retired)
            {
                var display = ActionCatalog.Find(key);
                var engine = ActionSemanticsCatalog.Find(key);
                if (display is null || engine is null)
                {
                    problems.Add($"{key} is missing from one of the catalogues");
                    continue;
                }

                if (display.Parameters.Any(parameter => parameter.Name == old))
                {
                    problems.Add($"{key} still draws a retired field named {old}");
                }

                if (engine.Parameter(old) is not null)
                {
                    problems.Add($"{key} still reads a retired field named {old}");
                }
            }
        });

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    /// <summary>
    /// Reads the retained-name table out of the developer notes, which is where a reason is
    /// written for a name whose labels or choices deliberately differ between actions.
    /// </summary>
    private static Dictionary<string, string> Whitelist()
    {
        var notes = File.ReadAllLines(Path.Combine(Repository(), "doc", "dev.md"));
        var start = Array.FindIndex(notes,
            line => line.StartsWith("**保留同名**", StringComparison.Ordinal));
        Assert.True(start >= 0, "the developer notes have no retained-name table");

        var end = Array.FindIndex(notes, start + 1,
            line => line.StartsWith("## ", StringComparison.Ordinal));
        Assert.True(end > start, "the retained-name table has no end");

        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in notes[(start + 1)..end])
        {
            var cells = line.Split('|', StringSplitOptions.None);
            if (cells.Length < 4)
            {
                continue;
            }

            var reason = cells[2].Trim();
            foreach (var written in cells[1].Split('、', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = written.Trim().Trim('`', ' ');
                if (name.Length == 0 || name is "名字" || name.StartsWith("---", StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.True(reason.Length > 0, $"{name} is retained without a reason");
                table[name] = reason;
            }
        }

        Assert.NotEmpty(table);
        return table;
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
