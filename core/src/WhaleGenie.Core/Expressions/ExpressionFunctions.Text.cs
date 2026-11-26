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

// Working on text and on numbers: length, fields, parts, formatting, and arithmetic.
internal static partial class FunctionLibrary
{
    private static double Length(Value value) => value.Kind switch
    {
        ValueKind.List => value.Items.Count,
        ValueKind.Record => value.Fields.Count,
        ValueKind.Null => 0,
        _ => value.AsText().Length,
    };

    /// <summary>
    /// One field of a record. A name the record does not have, or a value that is not a record at
    /// all, is a mistake worth saying out loud — and the message lists the fields the record does
    /// have, because those names come from the action that made it and nobody remembers them all.
    /// </summary>
    private static Value Field(IReadOnlyList<Func<Value>> args)
    {
        var record = V(args, 0);
        if (record.Kind is not ValueKind.Record)
        {
            throw ExpressionException.TypeMismatch($"\"{record.AsText()}\" is not a record");
        }

        var name = S(args, 1).Trim();
        if (record.Field(name) is { } found)
        {
            return found;
        }

        var names = string.Join(", ", record.Fields.Select(field => field.Name));
        throw ExpressionException.TypeMismatch(names.Length == 0
            ? $"a record has no field \"{name}\"; it has no fields at all"
            : $"a record has no field \"{name}\"; it has {names}");
    }

    private static string Substring(IReadOnlyList<Func<Value>> args)
    {
        var text = S(args, 0);
        var start = (int)N(args, 1);
        if (start < 0)
        {
            start += text.Length;
        }

        start = Math.Clamp(start, 0, text.Length);
        if (args.Count < 3)
        {
            return text[start..];
        }

        var length = Math.Clamp((int)N(args, 2), 0, text.Length - start);
        return text.Substring(start, length);
    }

    private static IEnumerable<Value> Split(IReadOnlyList<Func<Value>> args)
    {
        var text = S(args, 0);
        var separator = S(args, 1);
        var parts = separator.Length == 0
            ? text.Select(character => character.ToString())
            : text.Split(separator, StringSplitOptions.None);

        return parts.Select(part => Value.FromText(part));
    }

    private static bool Contains(Value value, string needle) => value.IsList
        ? value.Items.Any(item => item.AsText().Contains(needle, StringComparison.OrdinalIgnoreCase))
        : value.AsText().Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static Value Compare(IReadOnlyList<Func<Value>> args)
    {
        var comparison = S(args, 1).Trim();
        var wanted = V(args, 2);
        var caseSensitive = args.Count > 3 && V(args, 3).AsBool();
        var trim = args.Count > 4 ? V(args, 4).AsBool() : (bool?)null;
        var multiline = args.Count > 5 && V(args, 5).AsBool();

        if (IsNumberWay(comparison))
        {
            if (!TryNumber(V(args, 0), out var value) || !TryNumber(wanted, out var number))
            {
                throw ExpressionException.TypeMismatch(
                    $"\"{comparison}\" compares numbers");
            }

            return Value.FromBool(NumberMatches(value, number, comparison));
        }

        var way = TextWay(comparison);
        var options = new TextCompareOptions(caseSensitive, trim, multiline);
        try
        {
            return Value.FromBool(TextCompare.Matches(V(args, 0).AsText(), wanted.AsText(), way,
                options));
        }
        catch (ArgumentException failure)
        {
            throw ExpressionException.TypeMismatch(failure.Message);
        }
    }

    private static double IndexOf(Value value, string needle)
    {
        if (!value.IsList)
        {
            return value.AsText().IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }

        for (var index = 0; index < value.Items.Count; index++)
        {
            if (value.Items[index].AsText().Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Repeat(string text, int times)
    {
        if (times <= 0 || text.Length == 0)
        {
            return string.Empty;
        }

        if ((long)times * text.Length > 1_000_000)
        {
            throw ExpressionException.TypeMismatch("the repeated text would be too long");
        }

        return string.Concat(Enumerable.Repeat(text, times));
    }

    private static string Format(IReadOnlyList<Func<Value>> args)
    {
        var template = S(args, 0);
        for (var index = 1; index < args.Count; index++)
        {
            template = template.Replace("{" + (index - 1) + "}", S(args, index), StringComparison.Ordinal);
        }

        return template;
    }

    private static string Extract(IReadOnlyList<Func<Value>> args)
    {
        var match = RegexGuard.Match(S(args, 0), S(args, 1));
        return match.Success ? match.Value : string.Empty;
    }

    private static double Round(IReadOnlyList<Func<Value>> args)
    {
        var value = N(args, 0);
        return args.Count < 2
            ? Math.Round(value, MidpointRounding.AwayFromZero)
            : Math.Round(value, Math.Clamp((int)N(args, 1), 0, 15), MidpointRounding.AwayFromZero);
    }

    private static double Random(Random dice, IReadOnlyList<Func<Value>> args) => args.Count switch
    {
        0 => dice.NextDouble(),
        1 => dice.Next((int)N(args, 0) + 1),
        _ => MinMax(dice, args),
    };

    private static double MinMax(Random dice, IReadOnlyList<Func<Value>> args)
    {
        var min = (int)N(args, 0);
        var max = (int)N(args, 1);
        return min > max ? min : dice.Next(min, max + 1);
    }
}
