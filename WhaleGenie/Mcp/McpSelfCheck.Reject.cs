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

// Drafts a validator should refuse, and the answers that say why.
internal sealed partial class McpSelfCheck
{
    private async Task RejectAsync(CancellationToken token)
    {
        var opened = await InvokeAsync("whalegenie_open_project", [], token).ConfigureAwait(false);
        var draft = Field(opened.Text, "draftId");
        if (string.IsNullOrWhiteSpace(draft))
        {
            Add("reject.open", McpCheckResult.Fail, opened.Text);
            return;
        }

        _drafts.Add(draft!);

        // Every probe carries a step id, and what is looked for is the code rather than the
        // sentence: the sentence is in the interface's language, so a client that compared sentences
        // would break the day someone translates it. The step id stays in the answer for the reader.
        var cases = new (string Check, string Token, string Id, string Macro)[]
        {
            ("reject.action_key", "UnknownAction", "aa22",
                """{"name":"Probe","steps":[{"type":"control.delai","id":"aa22","params":{"ms":"500"}}]}"""),
            ("reject.child_keys", "CaseOutsideSwitch", "bb22",
                """{"name":"Probe","steps":[{"type":"control.case","id":"bb22","params":{"values":"1","body":[]}}]}"""),
            ("reject.record_field", "WindowBounds", "cc22",
                """{"name":"Probe","steps":[{"type":"window.move","id":"cc22","params":{"rect":{"left":1,"top":2,"height":100}}}]}"""),
            ("reject.enum_value", "BadOption", "dd22",
                """{"name":"Probe","steps":[{"type":"control.switch","id":"dd22","params":{"switchValue":"1","switchMode":"bogus","cases":[{"type":"control.case","params":{"values":"1","body":[]}}]}}]}"""),
        };

        foreach (var (check, word, id, macro) in cases)
        {
            var written = await InvokeAsync("whalegenie_put_macro",
                MacroArguments(draft!, JsonNode.Parse(macro)!.AsObject()), token).ConfigureAwait(false);
            if (written.Failed)
            {
                Add(check, McpCheckResult.Pass, $"refused at the write: {written.Text}");
                continue;
            }

            var validated = await InvokeAsync("whalegenie_validate",
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
                .ConfigureAwait(false);
            var problems = Problems(validated.Text, "blocking", "messages");
            var named = problems.FirstOrDefault(problem => Mentions(problem, word))
                ?? problems.FirstOrDefault(problem => Mentions(problem, id));
            Add(check, named is not null ? McpCheckResult.Pass
                    : problems.Count == 0 ? McpCheckResult.Fail : McpCheckResult.Missing,
                named is not null
                    ? $"located it: {named}"
                    : problems.Count == 0
                        ? "the wrong macro was accepted without a word"
                        : $"reported, but neither \"{word}\" nor step {id} is named: {string.Join(" / ", problems)}");
        }

        // A value that is not JSON at all. Whatever parameter carries the macro, text that does not
        // parse has to come back as a refusal rather than a half-written macro.
        var name = Declares("whalegenie_put_macro", "macro") ? "macro" : "macroJson";
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft };
        arguments[name] = """{"name":""";
        var broken = await InvokeAsync("whalegenie_put_macro", arguments, token).ConfigureAwait(false);
        Add("reject.bad_json", broken.Failed ? McpCheckResult.Pass : McpCheckResult.Fail,
            broken.Failed ? broken.Text : "a truncated value was accepted");

        // What a macro takes and gives back lives in that macro, not in the catalogue: for a step
        // that calls it, the signature tool is the only place an outside client can read the names.
        var child = new JsonObject
        {
            ["name"] = "Child",
            ["arguments"] = new JsonArray(new JsonObject
            {
                ["name"] = "数量",
                ["type"] = "Number",
                ["required"] = true,
            }),
            ["returns"] = new JsonArray(new JsonObject { ["name"] = "答案", ["type"] = "Number" }),
            ["steps"] = new JsonArray(new JsonObject
            {
                ["type"] = "control.delay",
                ["params"] = new JsonObject { ["ms"] = "1" },
            }),
        };
        await InvokeAsync("whalegenie_put_macro", MacroArguments(draft!, child), token)
            .ConfigureAwait(false);
        var signature = await InvokeAsync("whalegenie_get_macro_signature",
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["draftId"] = draft,
                ["name"] = "Child",
            }, token).ConfigureAwait(false);
        // A call passes rows now, so what the signature has to say is which columns a row has and
        // what a skeleton of them looks like — the names themselves are in the list either way.
        var described = !signature.Failed
            && Mentions(signature.Text, "argumentColumns")
            && Mentions(signature.Text, "callTemplate")
            && Mentions(signature.Text, "数量");
        Add("signature.tool", described ? McpCheckResult.Pass : McpCheckResult.Missing,
            described ? "the signature names the argument columns and gives a call skeleton" : signature.Text);

        // The catalogue can only say where a call's parameters come from; that it says so, and that
        // the argument list is the shape a row list has, is what a client reads before writing one.
        var call = await InvokeAsync("whalegenie_get_action",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["key"] = "control.runMacro" },
            token).ConfigureAwait(false);
        var shaped = !call.Failed
            && Mentions(call.Text, "signatureSource")
            && Mentions(call.Text, "arguments")
            && Mentions(call.Text, "given");
        Add("action.callShape", shaped ? McpCheckResult.Pass : McpCheckResult.Missing,
            shaped ? "the catalogue names where a call's parameters come from and the rows they have"
                : call.Text);

        await InvokeAsync("whalegenie_discard",
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["draftId"] = draft }, token)
            .ConfigureAwait(false);
        _drafts.Remove(draft!);
    }
}
