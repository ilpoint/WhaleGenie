using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Tests;

// Two menu cells in one place, and a background input rule through a save.
public partial class MacroPackageFormatTests
{
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
                        Written = Formula.FromLine("background"),
                    },
                    new StepParameter
                    {
                        Name = "window",
                        Kind = ActionParameterKind.WindowRule,
                        Written = Formula.FromLine(rule.ToJson().ToJsonString()),
                    },
                    new StepParameter
                    {
                        Name = "key",
                        Kind = ActionParameterKind.Key,
                        Written = Formula.FromLine("F5"),
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
            Assert.True(WindowRule.TryParse(FormulaText.Write(executableWindow.Written),
                out var executableRule));
            Assert.Equal(rule, executableRule);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
