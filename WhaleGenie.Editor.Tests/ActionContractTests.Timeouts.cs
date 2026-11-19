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

// Timeouts and blocks, and the actions the runner actually dispatches.
public partial class ActionContractTests
{
    /// <summary>
    /// carrying a second timeout field would start a fresh clock on each retry, which is exactly
    /// what the user cannot see from the step's own settings.
    /// </summary>
    [Fact]
    public void No_action_carries_a_second_timeout_field()
    {
        foreach (var action in ActionSemanticsCatalog.All)
        {
            Assert.Null(action.Parameter("timeoutMs"));
            Assert.NotEqual(ActionTimeoutSource.Parameter, action.TimeoutSource);
        }

        // Looking for an element is a question about now, not a wait; waiting for one has its own
        // action and its own step timeout.
        var exists = ActionSemanticsCatalog.Find("uia.exists");
        Assert.NotNull(exists);
        Assert.False(exists.Waits);
        Assert.Equal(ActionTimeoutSource.None, exists.TimeoutSource);
    }

    /// <summary>
    /// The two things a user has to know before choosing a timeout: every try spends the same
    /// budget, and retrying a block runs its completed steps over again.
    /// </summary>
    [Fact]
    public void The_timeout_and_block_hints_say_what_a_retry_costs()
    {
        Assert.Contains("共用", Strings.Chinese["Add.StepTimeoutHint"], StringComparison.Ordinal);
        Assert.Contains("整块重来", Strings.Chinese["Add.BlockSettings"], StringComparison.Ordinal);

        var manual = ManualDocument.AllText();
        Assert.Contains("共用", manual, StringComparison.Ordinal);
        Assert.Contains("整块重来", manual, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_input_action_is_declared_and_no_reader_is_declared_as_one()
    {
        string[] sending =
        [
            "clipboard.copy", "clipboard.paste",
            "ocr.clickText",
            "uia.click",
            "vision.clickImage", "vision.clickDetect", "vision.clickColor",
        ];

        foreach (var key in sending)
        {
            Assert.True(ActionSemanticsCatalog.Find(key)!.SendsInput, key);
            Assert.True(MacroRunner.SendsInput(key), key);
        }

        foreach (var key in new[]
                 {
                     "input.keyPress", "input.mouseClick", "gamepad.button",
                     "vision.findImage", "browser.click", "uia.invoke",
                 })
        {
            Assert.Equal(key.StartsWith("input.", StringComparison.Ordinal)
                         || key.StartsWith("gamepad.", StringComparison.Ordinal),
                ActionSemanticsCatalog.Find(key)!.SendsInput);
        }

        Assert.False(MacroRunner.SendsInput("vision.findImage"));
        Assert.False(MacroRunner.SendsInput("not.an.action"));
    }

    [Fact]
    public void The_runner_dispatches_every_executable_action()
    {
        var folder = RepoPaths.Source("WhaleGenie.Core", "Execution");
        var sources = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();
        var dispatch = sources.SelectMany(FamilyDispatches).ToList();

        var executable = ActionSemanticsCatalog.All
            .Where(action => action.Key != "control.case")
            .Select(action => action.Key)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(executable, dispatch.Order(StringComparer.Ordinal));
    }
}
