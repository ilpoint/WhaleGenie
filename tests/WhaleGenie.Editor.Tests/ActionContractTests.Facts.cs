using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;


namespace WhaleGenie.Tests;

// What the catalogue is allowed to know: the engine facts, and which steps cannot be undone.
public partial class ActionContractTests
{
    /// <summary>
    /// Which steps cannot be undone is a fact about the action, so it is written in the engine's own
    /// catalogue rather than decided by a method in the editor. The method that used to decide read
    /// a field that had been renamed, so a step that shuts the machine down was never asked about.
    /// The list is short on purpose: a question that comes up every time is a question that has
    /// become one more keypress on the way to the work.
    /// </summary>
    [Fact]
    public void The_catalogue_says_which_steps_cannot_be_undone()
    {
        Assert.True(ActionSemanticsCatalog.Find("file.delete")!.Irreversible);
        Assert.True(ActionSemanticsCatalog.Find("file.deleteFolder")!.Irreversible);
        Assert.True(ActionSemanticsCatalog.Find("system.deleteRegistry")!.Irreversible);

        // A power step asks about what it was told to do: locking the screen costs nothing, and
        // cancelling a shutdown that is already counting down puts things back.
        var power = ActionSemanticsCatalog.Find("system.power")!;
        Assert.False(power.Irreversible);
        Assert.Equal(["hibernate", "restart", "shutDown", "signOut", "sleep"],
            power.Parameter("powerAction")!.IrreversibleWhen.Order(StringComparer.Ordinal).ToList());

        // 写到这台机器是另一条线：同一个动作，改的是谁的设置，代价不一样。
        var write = ActionSemanticsCatalog.Find("system.writeRegistry")!;
        Assert.False(write.Irreversible);
        Assert.Equal(["localMachine"],
            write.Parameter("hive")!.IrreversibleWhen.Order(StringComparer.Ordinal).ToList());

        // 写机器级环境变量同上：改的是此后这台机器上启动的每个程序。
        var scope = ActionSemanticsCatalog.Find("system.setEnvironment")!;
        Assert.Equal(["machine"],
            scope.Parameter("environmentScope")!.IrreversibleWhen.Order(StringComparer.Ordinal).ToList());

        var asking = ActionSemanticsCatalog.All
            .Where(action => action.Irreversible
                || action.Parameters.Any(parameter => parameter.IrreversibleWhen.Count > 0))
            .Select(action => action.Key)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["file.delete", "file.deleteFolder", "system.deleteRegistry", "system.power",
            "system.setEnvironment", "system.writeRegistry"], asking);
    }

    [Fact]
    public void The_display_catalogue_holds_no_engine_facts()
    {
        var display = File.ReadAllText(RepoPaths.Source("WhaleGenie.Domain", "Models",
            "ActionDisplay.json"));
        string[] engineFacts =
        [
            "AcceptsFormula", "Category", "DefaultValue", "Hidden", "NamesVariable", "Options", "Picker",
            "ValueForm", "Required", "SendsInput", "Shape", "StepTimeoutMs", "Waits",
            "ChildKeys", "TimeoutSource", "TimeoutField",
            "Unit", "EmptyMeans", "Minimum", "Maximum", "EmptiesToEdge", "IsDuration",
            "SignatureSource", "Irreversible", "IrreversibleWhen", "Table",
        ];

        using var document = JsonDocument.Parse(display);
        var seen = new List<string>();
        Keys(document.RootElement, seen);

        var duplicated = seen.Where(key => engineFacts.Contains(key, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(duplicated.Count == 0,
            "the display catalogue declares engine facts again: " + string.Join(", ", duplicated));

        var source = File.ReadAllText(RepoPaths.Source("WhaleGenie.Domain", "Models",
            "ActionCatalog.cs"));
        Assert.DoesNotContain("Key = \"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendsInput =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StepTimeoutMs =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ActionParameterSemantics", source, StringComparison.Ordinal);
    }
}
