using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;


namespace WhaleGenie.Mcp;

// Declaring an argument or a result, and ordering the declarations.
internal static partial class McpPatches
{
    /// <summary>
    /// Changes what the macro asks a call to fill in, or what it offers to hand back: add, update,
    /// remove or reorder one declaration. These are the fields a call site reads, and they had no
    /// way in short of sending the whole macro — which also lost every declaration nobody retyped.
    /// </summary>
    private static Refusal? Declare(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var kind = Text(operation["kind"]).Trim().ToLowerInvariant();
        if (kind is not ("argument" or "result"))
        {
            return Refused(McpErrors.BadArgument, $"\"{kind}\" is not a kind of declaration.",
                "kind is argument (what a call fills in) or result (what the macro hands back).",
                true, new { kind });
        }

        var action = Text(operation["action"]).Trim().ToLowerInvariant();
        if (action is not ("add" or "update" or "remove" or "move"))
        {
            return Refused(McpErrors.BadArgument, $"\"{action}\" is not a declaration change.",
                "action is add, update, remove or move.", true, new { action });
        }

        var name = Text(operation["name"]).Trim();
        if (name.Length == 0)
        {
            return Refused(McpErrors.BadArgument, "A declaration change needs name.",
                "name is the word the macro reads the value by, such as 数量.", true, null);
        }

        if (!VariableName.IsName(name))
        {
            return Refused(McpErrors.BadArgument, $"\"{name}\" cannot be read back as a variable.",
                "A name is letters, digits and underscores, and does not start with a digit.",
                true, new { name });
        }

        return kind == "argument"
            ? DeclareArgument(macro, operation, action, name, touches)
            : DeclareResult(macro, operation, action, name, touches);
    }

    /// <summary>One argument of a macro: what a call fills in.</summary>
    private static Refusal? DeclareArgument(MacroItem macro, JsonObject operation, string action,
        string name, List<Touch> touches)
    {
        var found = macro.Parameters.FindIndex(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));

        switch (action)
        {
            case "add":
                if (found >= 0)
                {
                    return Refused(McpErrors.BadArgument, $"\"{name}\" is already an argument.",
                        "Change it with action=update, or pick another name.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badType })
                {
                    return badType;
                }

                var added = new MacroParameter { Name = name };
                if (Fill(added, operation) is { } refusal)
                {
                    return refusal;
                }

                macro.Parameters.Add(added);
                touches.Add(new Touch("declare", null, "argument:" + name));
                return null;

            case "update":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no argument named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badUpdate })
                {
                    return badUpdate;
                }

                if (Fill(macro.Parameters[found], operation) is { } failed)
                {
                    return failed;
                }

                touches.Add(new Touch("declare", null, "argument:" + name));
                return null;

            case "remove":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no argument named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                macro.Parameters.RemoveAt(found);
                touches.Add(new Touch("declare", null, "argument:" + name));
                return null;

            default:
                return Move(macro.Parameters, found, name, operation, "argument", touches);
        }
    }

    /// <summary>One result of a macro: what a call may ask it to hand back.</summary>
    private static Refusal? DeclareResult(MacroItem macro, JsonObject operation, string action,
        string name, List<Touch> touches)
    {
        var found = macro.Results.FindIndex(result =>
            string.Equals(result.Name, name, StringComparison.Ordinal));

        switch (action)
        {
            case "add":
                if (found >= 0)
                {
                    return Refused(McpErrors.BadArgument, $"\"{name}\" is already a result.",
                        "Change it with action=update, or pick another name.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badType })
                {
                    return badType;
                }

                var added = new MacroResult { Name = name };
                if (Fill(added, operation) is { } refusal)
                {
                    return refusal;
                }

                macro.Results.Add(added);
                touches.Add(new Touch("declare", null, "result:" + name));
                return null;

            case "update":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no result named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badUpdate })
                {
                    return badUpdate;
                }

                if (Fill(macro.Results[found], operation) is { } failed)
                {
                    return failed;
                }

                touches.Add(new Touch("declare", null, "result:" + name));
                return null;

            case "remove":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no result named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                macro.Results.RemoveAt(found);
                touches.Add(new Touch("declare", null, "result:" + name));
                return null;

            default:
                return Move(macro.Results, found, name, operation, "result", touches);
        }
    }
}
