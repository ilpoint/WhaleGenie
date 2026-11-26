using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Mcp;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp.Tests;

/// <summary>
/// The pure draft validation has no draft object, lock or project: two named macros in a list are
/// enough to prove the blocking rules before any session is touched.
/// </summary>
public class McpDraftRulesTests
{
    [Fact]
    public void The_preview_diff_is_a_function_of_the_two_snapshots()
    {
        var before = new MacroItem { Name = "kept" };
        var after = new MacroItem { Name = "kept" };
        after.Steps.Add(new MacroStep { Type = "control.log" });
        var added = new MacroItem { Name = "new" };

        var result = McpPreview.Of([before], [after, added], [], []);

        Assert.Equal(1, result.Totals.Added);
        Assert.Equal(1, result.Totals.Changed);
        Assert.Equal(0, result.Totals.Removed);
        Assert.Equal("new", result.Macros.Single(change => change.Change == "added").Name);
        Assert.Equal("kept", result.Macros.Single(change => change.Change == "changed").Name);
    }

    [Fact]
    public void A_client_macro_and_its_triggers_are_read_without_a_draft()
    {
        var input = McpDraftInput.ReadPut(new JsonObject
        {
            ["name"] = "  新宏  ",
            ["steps"] = new JsonArray(new JsonObject
            {
                ["type"] = "control.delay",
                ["params"] = new JsonObject { ["ms"] = "1" },
            }),
            ["triggers"] = new JsonArray(new JsonObject { ["kind"] = "hotkey" }),
        });

        Assert.Equal("新宏", input.Macro.Name);
        Assert.Single(input.Macro.Steps);
        Assert.Equal(string.Empty, input.Macro.Steps[0].Id);
        var trigger = Assert.Single(input.Triggers!);
        Assert.Equal("新宏", trigger["macro"]!.GetValue<string>());
        Assert.Null(trigger["id"]);
    }

    [Fact]
    public void Global_declarations_are_read_without_a_draft()
    {
        var globals = McpDraftInput.ReadGlobals(new JsonArray(new JsonObject
        {
            ["name"] = "count",
            ["type"] = "number",
            ["default"] = "3",
            ["description"] = "how many",
            ["persistent"] = true,
        }));

        var global = Assert.Single(globals);
        Assert.Equal("count", global.Name);
        Assert.Equal("how many", global.Description);
        Assert.True(global.PersistValue);
    }

    [Fact]
    public void A_duplicate_name_blocks_with_the_shared_name()
    {
        var result = McpDraftRules.Validate(
            [new MacroItem { Name = "same" }, new MacroItem { Name = "same" }], []);

        Assert.False(result.Ok);
        Assert.Contains(result.Blocking, problem => problem.Code == "MacroNameDuplicate"
            && problem.Macro == "same");
    }

    [Fact]
    public void An_empty_name_and_a_clean_macro_are_told_apart()
    {
        var empty = McpDraftRules.Validate([new MacroItem { Name = " " }], []);
        var clean = McpDraftRules.Validate([new MacroItem { Name = "ready" }], []);

        Assert.False(empty.Ok);
        Assert.Contains(empty.Blocking, problem => problem.Code == "MacroNameMissing");
        Assert.True(clean.Ok);
        Assert.Empty(clean.Blocking);
    }
}
