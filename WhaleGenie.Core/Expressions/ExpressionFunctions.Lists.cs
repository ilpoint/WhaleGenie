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

// Working on lists: adding, removing, ordering, slicing, filtering and reading a column.
internal static partial class FunctionLibrary
{
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

    /// <summary>
    /// Keeps the records whose named field answers one comparison. Text is held against the same
    /// five words every other comparison uses; numbers are held against the six order words. A row
    /// that cannot answer — it is not a record, has no such field, or holds text where a number
    /// was asked for — is left out and reported rather than read as zero.
    /// </summary>
    private static Value Where(IReadOnlyList<Func<Value>> args)
    {
        var rows = L(args, 0);
        var field = S(args, 1).Trim();
        if (field.Length == 0)
        {
            throw ExpressionException.TypeMismatch("where needs the name of a field to compare");
        }

        var compare = S(args, 2).Trim();
        var wanted = V(args, 3);
        var numeric = IsNumberWay(compare);
        var options = new TextCompareOptions(
            args.Count > 4 && V(args, 4).AsBool(),
            args.Count > 5 ? V(args, 5).AsBool() : null);

        double wantedNumber = 0;
        string way = compare;
        if (numeric)
        {
            wantedNumber = AskedForNumber(wanted, compare);
        }
        else
        {
            way = TextWay(compare);
            try
            {
                TextCompare.Check(wanted.AsText(), way, options);
            }
            catch (ArgumentException failure)
            {
                throw ExpressionException.TypeMismatch(failure.Message);
            }
        }

        var kept = new List<Value>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!row.IsRecord)
            {
                Expression.Report(LogKeys.ListRowNotRecord, index + 1);
                continue;
            }

            if (row.Field(field) is not { } value)
            {
                Expression.Report(LogKeys.ListFieldMissing, index + 1, field);
                continue;
            }

            if (numeric)
            {
                if (!TryNumber(value, out var number))
                {
                    Expression.Report(LogKeys.ListFieldNotNumber, index + 1, field, value.AsText());
                    continue;
                }

                if (NumberMatches(number, wantedNumber, compare))
                {
                    kept.Add(row);
                }

                continue;
            }

            if (TextCompare.Matches(value.AsText(), wanted.AsText(), way, options))
            {
                kept.Add(row);
            }
        }

        return Value.FromList(kept);
    }

    /// <summary>
    /// One value per record from the same field. Rows without the field are left out and reported;
    /// a list in which no record has it says which fields the records do have, because those names
    /// come from the action that made the records.
    /// </summary>
    private static Value Column(IReadOnlyList<Func<Value>> args)
    {
        var rows = L(args, 0);
        var field = S(args, 1).Trim();
        if (field.Length == 0)
        {
            throw ExpressionException.TypeMismatch("column needs the name of a field to read");
        }

        var values = new List<Value>();
        var found = false;
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!row.IsRecord)
            {
                Expression.Report(LogKeys.ListRowNotRecord, index + 1);
                continue;
            }

            if (row.Field(field) is not { } value)
            {
                Expression.Report(LogKeys.ListFieldMissing, index + 1, field);
                continue;
            }

            found = true;
            values.Add(value);
        }

        if (found || rows.Count == 0)
        {
            return Value.FromList(values);
        }

        var names = rows.FirstOrDefault(row => row.IsRecord)?.Fields
            .Select(one => one.Name)
            .ToList() ?? [];
        throw ExpressionException.TypeMismatch(names.Count == 0
            ? $"no record in the list has a field \"{field}\"; the records have no fields"
            : $"no record in the list has a field \"{field}\"; they have {string.Join(", ", names)}");
    }
}
