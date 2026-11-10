using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WhaleGenie.Core.Execution;


namespace WhaleGenie.Mcp;

// The small macro a self-check writes into the project, and the arguments it passes to it.
internal sealed partial class McpSelfCheck
{
    /// <summary>An if, a loop and a delay: the smallest macro that needs a branch, a nested list and
    /// a number with a unit.</summary>
    private JsonObject Scenario()
    {
        static JsonObject Step(string type, JsonObject parameters) => new()
        {
            ["type"] = type,
            ["params"] = parameters,
        };

        return new JsonObject
        {
            ["name"] = _options.ScratchMacro,
            ["steps"] = new JsonArray(
                Step("control.if", new JsonObject
                {
                    ["condition"] = "$sys.screenWidth > 1",
                    ["then"] = new JsonArray(Step("control.delay", new JsonObject { ["ms"] = "200" })),
                    ["else"] = new JsonArray(),
                }),
                Step("control.repeat", new JsonObject
                {
                    ["times"] = "3",
                    ["intervalMs"] = "0",
                    ["body"] = new JsonArray(Step("control.delay", new JsonObject { ["ms"] = "100" })),
                })),
        };
    }

    /// <summary>
    /// The macro handed over in the argument the server declares: an object when the surface takes
    /// one, text when it still takes JSON inside a string. Reading the declared names is how the
    /// ruler follows the server instead of assuming a shape.
    /// </summary>
    private Dictionary<string, object?> MacroArguments(string draft, JsonObject macro)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft };
        var name = Declares("whalegenie_put_macro", "macro") ? "macro" : "macroJson";
        arguments[name] = name == "macro" ? macro : macro.ToJsonString();
        return arguments;
    }
}
