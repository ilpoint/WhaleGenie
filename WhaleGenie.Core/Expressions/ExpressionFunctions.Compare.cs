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

// How one value is compared with another, by number or by text.
internal static partial class FunctionLibrary
{
    private static bool IsNumberWay(string compare)
        => compare is ">" or ">=" or "<" or "<=" or "=" or "!=";

    private static bool NumberMatches(double value, double wanted, string compare) => compare switch
    {
        ">" => value > wanted,
        ">=" => value >= wanted,
        "<" => value < wanted,
        "<=" => value <= wanted,
        "=" => value == wanted,
        _ => value != wanted,
    };

    private static double AskedForNumber(Value value, string compare)
    {
        if (TryNumber(value, out var number))
        {
            return number;
        }

        throw ExpressionException.TypeMismatch(
            $"\"{compare}\" compares numbers, and \"{value.AsText()}\" is not one");
    }

    /// <summary>
    /// A number, or text a person would read as one. A CSV cell is text even when it says 42:
    /// refusing it here would make a numeric where over a table read from a file skip every row.
    /// Text that does not read as a number is still left out and reported.
    /// </summary>
    private static bool TryNumber(Value value, out double number)
    {
        if (value.IsNumber)
        {
            number = value.Number;
            return true;
        }

        if (value.Kind is ValueKind.Bool)
        {
            number = value.Flag ? 1 : 0;
            return true;
        }

        if (value.Kind is ValueKind.Text && ValueRules.TryNumber(value.Text, out var parsed))
        {
            number = parsed;
            return true;
        }

        number = 0;
        return false;
    }

    private static string TextWay(string compare)
    {
        try
        {
            return TextCompare.Read(compare);
        }
        catch (ArgumentException)
        {
            throw ExpressionException.TypeMismatch(
                $"\"{compare}\" is not a comparison; use {string.Join(", ", TextCompare.Values)} "
                + "or >, >=, <, <=, =, !=");
        }
    }
}
