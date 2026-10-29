using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// An export is an ordinary package with a few macros in it. What this checks is the cutting: which
/// macros, which of their trigger entries, and which of the project's global variables end up in
/// the file, and what happens to a call whose target was left behind.
/// </summary>
public class MacroBundleTests
{
    [Fact]
    public void Only_the_chosen_macros_are_written()
    {
        var folder = TempFolder();
        try
        {
            var one = new MacroItem { Name = "整理" };
            var two = new MacroItem { Name = "读表" };
            var path = Path.Combine(folder, "两个" + MacroPackage.Extension);

            MacroPackage.Save(path, MacroBundle.Copies([one]),
                MacroBundle.Take(new ProjectTriggers(), [one]), [], "0.0.0");

            var contents = MacroPackage.Load(path);
            var only = Assert.Single(contents.Macros);
            Assert.Equal("整理", only.Name);
            Assert.DoesNotContain(contents.Macros, macro => macro.Name == two.Name);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// A macro that was switched off arrives switched off, and the project's own object is not the
    /// one that was written: saving rewrites the trigger table of what it is handed, so handing it
    /// the project's macros would leave the project holding the export's trimmed table.
    /// </summary>
    [Fact]
    public void A_disabled_macro_stays_disabled_and_the_project_object_is_left_alone()
    {
        var folder = TempFolder();
        try
        {
            var macro = new MacroItem { Name = "暂停中", IsEnabled = false };
            var copy = Assert.Single(MacroBundle.Copies([macro]));
            Assert.NotSame(macro, copy);

            var path = Path.Combine(folder, "一个" + MacroPackage.Extension);
            MacroPackage.Save(path, [copy], MacroBundle.Take(new ProjectTriggers(), [macro]), [],
                "0.0.0");

            var only = Assert.Single(MacroPackage.Load(path).Macros);
            Assert.False(only.IsEnabled);
            Assert.False(macro.IsEnabled);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// The variables that travel are the ones the chosen macros read, not the project's whole table:
    /// exporting one macro should not drag in every name the rest of the project uses.
    /// </summary>
    [Fact]
    public void An_export_carries_only_the_globals_the_chosen_macros_read()
    {
        var previous = VariableCatalog.Globals.ToList();
        VariableCatalog.Globals.Clear();
        VariableCatalog.Globals.Add(new VariableDefinition
        {
            Name = "次数",
            Scope = VariableScope.Global,
            Type = "number",
            DefaultValue = "1",
        });
        VariableCatalog.Globals.Add(new VariableDefinition
        {
            Name = "无关紧要",
            Scope = VariableScope.Global,
            Type = "text",
            DefaultValue = "x",
        });

        try
        {
            var macro = new MacroItem { Name = "循环" };
            macro.Steps.Add(new MacroStep
            {
                Type = "control.delay",
                Parameters =
                [
                    new StepParameter { Name = "ms", Kind = ActionParameterKind.Text, Value = "$次数" },
                ],
            });

            var globals = MacroBundle.UsedGlobals([macro]);
            var only = Assert.Single(globals);
            Assert.Equal("次数", only.Name);
        }
        finally
        {
            VariableCatalog.Globals.Clear();
            foreach (var global in previous)
            {
                VariableCatalog.Globals.Add(global);
            }
        }
    }

    /// <summary>
    /// A macro arrives with the entries that start it — its hotkey, or its place on a menu page —
    /// and without the entries of the macros that stayed behind.
    /// </summary>
    [Fact]
    public void An_export_keeps_the_trigger_entries_of_the_chosen_macro()
    {
        var folder = TempFolder();
        try
        {
            var one = new MacroItem { Name = "整理" };
            var two = new MacroItem { Name = "读表" };
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Key,
                MacroName = "整理",
                BindKey = "Ctrl+Alt+1",
            });
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Key,
                MacroName = "读表",
                BindKey = "Ctrl+Alt+2",
            });

            var subset = MacroBundle.Take(triggers, [one]);
            var kept = Assert.Single(subset.Entries);
            Assert.Equal("整理", kept.MacroName);

            var path = Path.Combine(folder, "一个" + MacroPackage.Extension);
            MacroPackage.Save(path, MacroBundle.Copies([one]), subset, [], "0.0.0");

            var loaded = Assert.Single(MacroPackage.Load(path).Macros);
            var entry = Assert.Single(loaded.Triggers);
            Assert.Equal("Ctrl+Alt+1", entry.BindKey);
            Assert.DoesNotContain(loaded.Triggers, candidate => candidate.MacroName == two.Name);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// A call to a macro that is not in the file is named before it is written: the run would stop
    /// at that step with nothing to say for it. A call to a macro that is inside the selection —
    /// its own submacro — is fine, because the submacro travels inside its parent.
    /// </summary>
    [Fact]
    public void A_call_to_a_macro_that_was_not_chosen_is_reported()
    {
        var helper = new MacroItem { Name = "读表" };
        helper.Steps.Add(new MacroStep { Type = "control.delay" });

        var parent = new MacroItem { Name = "整理" };
        parent.SubMacros.Add(helper);
        parent.Steps.Add(Call("读表"));
        parent.Steps.Add(Call("收尾"));

        var missing = MacroBundle.DanglingCalls([parent]);

        Assert.Equal(["收尾"], missing);
    }

    /// <summary>
    /// A submacro ticked on its own leaves as a macro of its own, which is how one piece of a long
    /// macro is handed to somebody who does not need the rest of it.
    /// </summary>
    [Fact]
    public void A_submacro_can_be_written_on_its_own()
    {
        var folder = TempFolder();
        try
        {
            var sub = new MacroItem { Name = "读表" };
            sub.Steps.Add(new MacroStep { Type = "control.delay" });

            var path = Path.Combine(folder, "读表" + MacroPackage.Extension);
            MacroPackage.Save(path, MacroBundle.Copies([sub]),
                MacroBundle.Take(new ProjectTriggers(), [sub]), [], "0.0.0");

            var only = Assert.Single(MacroPackage.Load(path).Macros);
            Assert.Equal("读表", only.Name);
            Assert.Single(only.Steps);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static MacroStep Call(string name) => new()
    {
        Type = "control.runMacro",
        Parameters = [new StepParameter { Name = "macro", Kind = ActionParameterKind.Text, Value = name }],
    };

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
