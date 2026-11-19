using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The two rules a set of documents answers to: a new sub-macro gets a name nothing else has, and
/// renaming one follows the calls that pointed at it. Both are about the whole set rather than one
/// page, so they are checked without a window.
/// </summary>
public class EditorDocumentNamingTests
{
    [Fact]
    public void A_second_copy_gets_a_number_no_document_has()
    {
        Assert.Equal("login", EditorDocumentNames.Unique("login", []));

        // Case does not make a name different, and the number starts at two: the first copy is the
        // one already there.
        Assert.Equal("login (2)", EditorDocumentNames.Unique("login", ["Login"]));
        Assert.Equal("login (3)",
            EditorDocumentNames.Unique(" login ", ["login", "login (2)"]));

        // A name that is only spaces is not a name: the editor offers its own word for a sub-macro.
        Assert.Equal(Strings.Get("Editor.NewSubMacroName"), EditorDocumentNames.Unique("   ", []));
    }

    [Fact]
    public void A_call_inside_a_block_is_still_a_call()
    {
        var steps = Steps();

        Assert.True(EditorDocumentNames.Calls(steps, "child"));
        Assert.True(EditorDocumentNames.Calls(steps, "CHILD"));
        Assert.False(EditorDocumentNames.Calls(steps, "other"));
    }

    [Fact]
    public void Renaming_points_every_call_at_the_new_name_and_leaves_the_rest()
    {
        var steps = Steps();

        EditorDocumentNames.RenameCalls(steps, "child", "renamed");

        Assert.Equal("renamed", steps[0].Text("macro").Trim());
        Assert.Equal("renamed", Nested(steps).Text("macro").Trim());
        Assert.Equal("1 = 1", steps[1].Text("condition").Trim());
    }

    /// <summary>
    /// One call at the top level and one written inside a block, with the second spelling the name
    /// in another case: a rename has to follow both.
    /// </summary>
    private static IReadOnlyList<MacroStep> Steps()
        => MacroItem.FromJson(new JsonObject
        {
            ["name"] = "parent",
            ["steps"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "control.runMacro",
                    ["params"] = new JsonObject { ["macro"] = "child" },
                },
                new JsonObject
                {
                    ["type"] = "control.if",
                    ["params"] = new JsonObject
                    {
                        ["condition"] = "1 = 1",
                        ["then"] = new JsonArray(new JsonObject
                        {
                            ["type"] = "control.runMacro",
                            ["params"] = new JsonObject { ["macro"] = "CHILD" },
                        }),
                    },
                }),
        }).Steps;

    private static MacroStep Nested(IReadOnlyList<MacroStep> steps)
        => steps[1].StepLists.Single(list => list.Name == "then").Steps.Single();
}
