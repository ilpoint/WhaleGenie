using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Viktor.Core.Expressions;

/// <summary>Everything a user can see about a built-in expression function.</summary>
public sealed record ExpressionFunction(string Name, string Signature, string Description);

/// <summary>One entry of the built-in function table.</summary>
internal sealed record FunctionEntry(
    string Name,
    string Signature,
    string Description,
    int MinArguments,
    int MaxArguments,
    Func<IReadOnlyList<Func<Value>>, Value> Body);

/// <summary>
/// The functions an expression can call, kept in one table so the evaluator and the
/// list the editor suggests can never drift apart.
/// </summary>
internal static class FunctionLibrary
{
    private const int Any = int.MaxValue;

    private static readonly FunctionEntry[] Entries =
    [
        // -------------------------------------------------------------------- text
        Fn("upper", "upper(text)", "Write a value in capitals.", 1, 1,
            args => Value.FromText(S(args, 0).ToUpperInvariant())),
        Fn("lower", "lower(text)", "Write a value in small letters.", 1, 1,
            args => Value.FromText(S(args, 0).ToLowerInvariant())),
        Fn("trim", "trim(text)", "Drop the spaces from both ends.", 1, 1,
            args => Value.FromText(S(args, 0).Trim())),
        Fn("length", "length(value)", "How many characters, or how many items a list holds.", 1, 1,
            args => Value.FromNumber(Length(V(args, 0)))),
        Fn("substring", "substring(text, start [, length])", "Part of some text; start counts from 0.", 2, 3,
            args => Value.FromText(Substring(args))),
        Fn("replace", "replace(text, old, new)", "Swap every occurrence of one piece of text for another.", 3, 3,
            args => Value.FromText(S(args, 0).Replace(S(args, 1), S(args, 2), StringComparison.OrdinalIgnoreCase))),
        Fn("split", "split(text, separator)", "Break text into a list.", 2, 2,
            args => Value.FromList(Split(args))),
        Fn("join", "join(list, separator)", "Glue the items of a list into one text.", 2, 2,
            args => Value.FromText(string.Join(S(args, 1), L(args, 0).Select(item => item.AsText())))),
        Fn("contains", "contains(value, needle)", "True when the text, or a list item, holds the needle.", 2, 2,
            args => Value.FromBool(Contains(V(args, 0), S(args, 1)))),
        Fn("startsWith", "startsWith(text, prefix)", "True when the text begins with the prefix.", 2, 2,
            args => Value.FromBool(S(args, 0).StartsWith(S(args, 1), StringComparison.OrdinalIgnoreCase))),
        Fn("endsWith", "endsWith(text, suffix)", "True when the text ends with the suffix.", 2, 2,
            args => Value.FromBool(S(args, 0).EndsWith(S(args, 1), StringComparison.OrdinalIgnoreCase))),
        Fn("indexOf", "indexOf(value, needle)", "Where the needle sits, or -1 when it is missing.", 2, 2,
            args => Value.FromNumber(IndexOf(V(args, 0), S(args, 1)))),
        Fn("repeat", "repeat(text, times)", "Write the same text several times over.", 2, 2,
            args => Value.FromText(Repeat(S(args, 0), (int)N(args, 1)))),
        Fn("concat", "concat(value, ...)", "Join every argument into one text.", 1, Any,
            args => Value.FromText(string.Concat(args.Select(argument => argument().AsText())))),
        Fn("format", "format(template, ...)", "Replace {0}, {1} and so on in a template.", 1, Any,
            args => Value.FromText(Format(args))),
        Fn("match", "match(text, pattern)", "True when the regular expression matches the text.", 2, 2,
            args => Value.FromBool(Regex.IsMatch(S(args, 0), S(args, 1)))),
        Fn("extract", "extract(text, pattern)", "The first piece of text the regular expression matches.", 2, 2,
            args => Value.FromText(Extract(args))),

        // -------------------------------------------------------------------- maths
        Fn("abs", "abs(number)", "How far the number is from zero.", 1, 1,
            args => Value.FromNumber(Math.Abs(N(args, 0)))),
        Fn("round", "round(number [, digits])", "Round to a whole number, or to some decimal places.", 1, 2,
            args => Value.FromNumber(Round(args))),
        Fn("floor", "floor(number)", "Round down.", 1, 1,
            args => Value.FromNumber(Math.Floor(N(args, 0)))),
        Fn("ceil", "ceil(number)", "Round up.", 1, 1,
            args => Value.FromNumber(Math.Ceiling(N(args, 0)))),
        Fn("sqrt", "sqrt(number)", "Square root.", 1, 1,
            args => Value.FromNumber(Math.Sqrt(N(args, 0)))),
        Fn("pow", "pow(number, power)", "Raise a number to a power.", 2, 2,
            args => Value.FromNumber(Math.Pow(N(args, 0), N(args, 1)))),
        Fn("min", "min(number, ...)", "The smallest of the arguments.", 1, Any,
            args => Value.FromNumber(args.Select(argument => argument().AsNumber()).Min())),
        Fn("max", "max(number, ...)", "The largest of the arguments.", 1, Any,
            args => Value.FromNumber(args.Select(argument => argument().AsNumber()).Max())),
        Fn("sum", "sum(list)", "Add up every number in a list.", 1, 1,
            args => Value.FromNumber(L(args, 0).Sum(item => item.AsNumber()))),
        Fn("random", "random([min, max])", "A random number; with bounds, a whole number in that range.", 0, 2,
            args => Value.FromNumber(Random(args))),

        // -------------------------------------------------------------------- lists
        Fn("list", "list(...values)", "Build a list out of the arguments.", 0, Any,
            args => Value.FromList(args.Select(argument => argument()))),
        Fn("count", "count(list)", "How many items a list holds.", 1, 1,
            args => Value.FromNumber(V(args, 0).AsList().Count)),
        Fn("append", "append(list, ...values)", "A copy of the list with values added at the end.", 1, Any,
            args => Append(args)),
        Fn("insertAt", "insertAt(list, index, value)", "A copy with one value inserted at the index.", 3, 3,
            args => InsertAt(args)),
        Fn("removeAt", "removeAt(list, index)", "A copy without the item at the index.", 2, 2,
            args => RemoveAt(args)),
        Fn("get", "get(list, index)", "The item at the index.", 2, 2,
            args => At(L(args, 0), (int)N(args, 1), "get")),
        Fn("first", "first(list)", "The first item.", 1, 1,
            args => At(L(args, 0), 0, "first")),
        Fn("last", "last(list)", "The last item.", 1, 1,
            args => At(L(args, 0), -1, "last")),
        Fn("sort", "sort(list)", "A copy sorted from small to large.", 1, 1,
            args => Value.FromList(Sort(L(args, 0), descending: false))),
        Fn("sortDesc", "sortDesc(list)", "A copy sorted from large to small.", 1, 1,
            args => Value.FromList(Sort(L(args, 0), descending: true))),
        Fn("reverse", "reverse(list)", "A copy with the items in the other order.", 1, 1,
            args => Value.FromList(L(args, 0).Reverse())),
        Fn("unique", "unique(list)", "A copy without the repeated items.", 1, 1,
            args => Value.FromList(Unique(L(args, 0)))),
        Fn("slice", "slice(list, start [, length])", "Part of a list; start counts from 0.", 2, 3,
            args => Slice(args)),

        // ------------------------------------------------------------- conversion
        Fn("number", "number(value)", "Read a value as a number.", 1, 1,
            args => Value.FromNumber(N(args, 0))),
        Fn("text", "text(value)", "Read a value as text.", 1, 1,
            args => Value.FromText(S(args, 0))),
        Fn("bool", "bool(value)", "Read a value as true or false.", 1, 1,
            args => Value.FromBool(V(args, 0).AsBool())),
        Fn("if", "if(condition, whenTrue, whenFalse)", "Pick one of two values.", 3, 3,
            args => V(args, 0).AsBool() ? V(args, 1) : V(args, 2)),
    ];

    private static readonly Dictionary<string, FunctionEntry> ByName =
        Entries.ToDictionary(entry => entry.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Names and signatures the expression editor offers as suggestions.</summary>
    public static IReadOnlyList<ExpressionFunction> Metadata { get; } =
        [.. Entries.Select(entry => new ExpressionFunction(entry.Name, entry.Signature, entry.Description))];

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

    private static FunctionEntry Fn(string name, string signature, string description, int min, int max,
        Func<IReadOnlyList<Func<Value>>, Value> body)
        => new(name, signature, description, min, max, body);

    private static Value V(IReadOnlyList<Func<Value>> args, int index) => args[index]();

    private static string S(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsText();

    private static double N(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsNumber();

    private static IReadOnlyList<Value> L(IReadOnlyList<Func<Value>> args, int index) => args[index]().AsList();

    private static List<Value> Copy(IReadOnlyList<Func<Value>> args, int index) => [.. args[index]().AsList()];

    private static double Length(Value value) => value.Kind switch
    {
        ValueKind.List => value.Items.Count,
        ValueKind.Null => 0,
        _ => value.AsText().Length,
    };

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
        var match = Regex.Match(S(args, 0), S(args, 1));
        return match.Success ? match.Value : string.Empty;
    }

    private static double Round(IReadOnlyList<Func<Value>> args)
    {
        var value = N(args, 0);
        return args.Count < 2
            ? Math.Round(value, MidpointRounding.AwayFromZero)
            : Math.Round(value, Math.Clamp((int)N(args, 1), 0, 15), MidpointRounding.AwayFromZero);
    }

    private static double Random(IReadOnlyList<Func<Value>> args) => args.Count switch
    {
        0 => System.Random.Shared.NextDouble(),
        1 => System.Random.Shared.Next((int)N(args, 0) + 1),
        _ => MinMax(args),
    };

    private static double MinMax(IReadOnlyList<Func<Value>> args)
    {
        var min = (int)N(args, 0);
        var max = (int)N(args, 1);
        return min > max ? min : System.Random.Shared.Next(min, max + 1);
    }

    private static Value Append(IReadOnlyList<Func<Value>> args)
    {
        var items = Copy(args, 0);
        for (var index = 1; index < args.Count; index++)
        {
            items.Add(args[index]());
        }

        return Value.FromList(items);
    }

    private static Value InsertAt(IReadOnlyList<Func<Value>> args)
    {
        var items = Copy(args, 0);
        items.Insert(Normalize((int)N(args, 1), items.Count, allowEnd: true), args[2]());
        return Value.FromList(items);
    }

    private static Value RemoveAt(IReadOnlyList<Func<Value>> args)
    {
        var items = Copy(args, 0);
        items.RemoveAt(Normalize((int)N(args, 1), items.Count, allowEnd: false));
        return Value.FromList(items);
    }

    private static Value At(IReadOnlyList<Value> items, int index, string name)
    {
        try
        {
            return items[Normalize(index, items.Count, allowEnd: false)];
        }
        catch (ExpressionException)
        {
            throw ExpressionException.IndexOutOfRange($"{name}({index})");
        }
    }

    /// <summary>Turns a possibly negative index into a real one, or refuses it.</summary>
    private static int Normalize(int index, int count, bool allowEnd)
    {
        if (index < 0)
        {
            index += count;
        }

        var limit = allowEnd ? count : count - 1;
        if (index < 0 || index > limit)
        {
            throw ExpressionException.IndexOutOfRange(index.ToString(CultureInfo.InvariantCulture));
        }

        return index;
    }

    private static IEnumerable<Value> Sort(IReadOnlyList<Value> items, bool descending)
    {
        IEnumerable<Value> ordered = items.All(item => item.IsNumber)
            ? items.OrderBy(item => item.Number)
            : items.OrderBy(item => item.AsText(), StringComparer.OrdinalIgnoreCase);

        return descending ? ordered.Reverse() : ordered;
    }

    private static IEnumerable<Value> Unique(IReadOnlyList<Value> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (seen.Add(item.AsText()))
            {
                yield return item;
            }
        }
    }

    private static Value Slice(IReadOnlyList<Func<Value>> args)
    {
        var items = L(args, 0);
        var start = (int)N(args, 1);
        if (start < 0)
        {
            start += items.Count;
        }

        start = Math.Clamp(start, 0, items.Count);
        var length = args.Count < 3
            ? items.Count - start
            : Math.Clamp((int)N(args, 2), 0, items.Count - start);

        return Value.FromList(items.Skip(start).Take(length));
    }
}
