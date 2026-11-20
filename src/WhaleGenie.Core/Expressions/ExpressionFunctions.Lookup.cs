using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;


namespace WhaleGenie.Core.Expressions;

// Finding a function by name, the arguments it takes, and the sentences its signature implies.
internal static partial class FunctionLibrary
{
    private static readonly Dictionary<string, FunctionEntry> ByName =
        Entries.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The few arguments whose own sentence says more than the shared one for their name does,
    /// keyed by "function.argument". Everything else about an argument — what it is called in the
    /// reader's language, and the line saying what goes in it — comes from the shared table.
    /// </summary>
    private static readonly Dictionary<string, string> OwnHints = new(StringComparer.Ordinal)
    {
        ["replaceRegex.pattern"] = "The regular expression to swap out.",
        ["insertAt.value"] = "The value to insert.",
        ["jsonSet.value"] = "The value to set it to.",
        ["jsonOf.name"] = "The name of the field to write.",
        ["jsonOf.value"] = "The value to write in that field.",
        ["field.name"] = "The name of the field to read, such as x.",
    };

    /// <summary>The words a comparison argument may hold: the text ways and the number ways.</summary>
    private static readonly string[] ComparisonWays =
        ["contains", "startsWith", "endsWith", "equals", "regex", ">", ">=", "<", "<=", "=", "!="];

    /// <summary>
    /// Which arguments are not free text. The name says what the argument is for and the engine reads
    /// the same names everywhere, so the shape is read off it rather than written out per function.
    /// </summary>
    private static (ExpressionParameterKind Kind, IReadOnlyList<string> Choices) ShapeOf(string name)
        => name switch
        {
            "caseSensitive" or "trim" or "multiline" or "descending" =>
                (ExpressionParameterKind.Flag, []),
            "comparison" => (ExpressionParameterKind.Choice, ComparisonWays),
            _ => (ExpressionParameterKind.Any, []),
        };

    /// <summary>Names and signatures the expression editor offers as suggestions.</summary>
    public static IReadOnlyList<ExpressionFunction> Metadata { get; } =
        [.. Entries.Select(entry =>
            new ExpressionFunction(entry.Name, entry.Signature, entry.Description, entry.Group,
                [.. ArgumentNames(entry.Signature).Select(name =>
                {
                    var (kind, choices) = ShapeOf(name);
                    return new ExpressionParameter(name, ArgumentLibrary.Label(name),
                        HintOf(entry.Name, name), kind, choices);
                })],
                entry.MinArguments,
                entry.MaxArguments))];

    /// <summary>
    /// The arguments a signature spells out, in the order they are written. The signature is what
    /// the editor shows and the engine checks, so the names come from there rather than from a
    /// second list that could drift away from it.
    /// </summary>
    private static IReadOnlyList<string> ArgumentNames(string signature)
    {
        var open = signature.IndexOf('(');
        var close = signature.LastIndexOf(')');
        if (open < 0 || close <= open)
        {
            return [];
        }

        var names = new List<string>();
        foreach (var part in signature[(open + 1)..close].Split(','))
        {
            // The brackets say an argument may be left out and the dots say more of the same may
            // follow; neither is part of the name.
            var name = part.Replace("[", string.Empty, StringComparison.Ordinal)
                .Replace("]", string.Empty, StringComparison.Ordinal)
                .Replace(".", string.Empty, StringComparison.Ordinal)
                .Trim();
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>What goes in an argument: its own line where it has one, the shared one otherwise.</summary>
    private static string HintOf(string function, string name)
        => OwnHints.TryGetValue($"{function}.{name}", out var own)
            ? own
            : ArgumentLibrary.Hint(name);

    public static bool TryGet(string name, out FunctionEntry entry)
    {
        if (ByName.TryGetValue(name, out var found))
        {
            entry = found;
            return true;
        }

        entry = null!;
        return false;
    }

    private static FunctionEntry Fn(string group, string name, string signature, string description,
        int min, int max, Func<IReadOnlyList<Func<Value>>, Value> body)
        => new(group, name, signature, description, min, max, body);

    private static Value V(IReadOnlyList<Func<Value>> args, int index) => args[index]();

    private static string S(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsText();

    private static double N(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsNumber();

    private static IReadOnlyList<Value> L(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsList();

    private static List<Value> Copy(IReadOnlyList<Func<Value>> args, int index) => [.. args[index]().AsList()];
}
