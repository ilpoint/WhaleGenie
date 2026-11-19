using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Runtime;

/// <summary>
/// Whether a binding answers to a key that was just pressed, with the named modifiers held. Both
/// the trigger service and the window's own hotkeys ask this one question, so a binding means the
/// same thing wherever it is written.
/// </summary>
public static class TriggerKeys
{
    /// <summary>
    /// Whether a binding answers to a key, with the named modifiers down. The words the binding is
    /// asked about are plain names, so this is where the two sides of a modifier and the older
    /// side-agnostic spelling are sorted out.
    /// </summary>
    public static bool Binds(string binding, string key, IEnumerable<string> held)
    {
        var parts = binding.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !Same(parts[^1], key))
        {
            return false;
        }

        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (!held.Any(heldKey => Same(parts[index], heldKey)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Compares two key names. The key picker spells the top row the way the interface library
    /// does (<c>D7</c>) while the hook calls the same key <c>7</c>, and a binding says which
    /// shift it means (<c>右Shift</c>) unless it was written before the sides were told apart.
    /// </summary>
    public static bool Same(string? first, string? second)
    {
        var left = Alias(first);
        var right = Alias(second);

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A binding of just "Shift" is the older, side-agnostic spelling: it answers to whichever
        // shift is pressed, so the macros written before the sides existed keep working.
        return StandsFor(left, right) || StandsFor(right, left);
    }

    /// <summary>Whether a bare modifier name means the side the other name spells out.</summary>
    private static bool StandsFor(string bare, string sided)
        => Sides.TryGetValue(bare, out var sides)
           && sides.Contains(sided, StringComparer.OrdinalIgnoreCase);

    /// <summary>The halves a bare modifier name stands for.</summary>
    private static readonly Dictionary<string, string[]> Sides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = ["左ctrl", "右ctrl"],
        ["control"] = ["左ctrl", "右ctrl"],
        ["shift"] = ["左shift", "右shift"],
        ["alt"] = ["左alt", "右alt"],
        ["win"] = ["左win", "右win"],
        ["windows"] = ["左win", "右win"],
        ["meta"] = ["左win", "右win"],
        ["super"] = ["左win", "右win"],
    };

    /// <summary>Trims a name to the spelling the comparisons use.</summary>
    private static string Alias(string? name)
    {
        var text = (name ?? string.Empty).Trim();

        // The key picker spells the top row the way the interface library does (D7), while the
        // hook calls the same key 7.
        if (text.Length == 2 && (text[0] is 'D' or 'd') && char.IsAsciiDigit(text[1]))
        {
            return text[1..];
        }

        // The sided names are written with Chinese words in the interface and with English words
        // in hand-written macros; both have to compare as the same key.
        return text.ToLowerInvariant() switch
        {
            "leftctrl" or "leftcontrol" or "lctrl" => "左ctrl",
            "rightctrl" or "rightcontrol" or "rctrl" => "右ctrl",
            "leftshift" or "lshift" => "左shift",
            "rightshift" or "rshift" => "右shift",
            "leftalt" or "lalt" => "左alt",
            "rightalt" or "ralt" => "右alt",
            "leftwin" or "leftmeta" or "lwin" => "左win",
            "rightwin" or "rightmeta" or "rwin" => "右win",
            _ => text.ToLowerInvariant(),
        };
    }
}
