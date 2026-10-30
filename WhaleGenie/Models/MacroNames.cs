using System;
using System.Collections.Generic;
using System.Globalization;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// The name a macro gets before its author has chosen one.
/// </summary>
/// <remarks>
/// A macro is called by its name, so an empty first name leaves a new macro unable to save. The
/// default is deliberately a sequence rather than a count: deleting a macro must not make the next
/// default equal to one that is still in the project.
/// </remarks>
public static class MacroNames
{
    /// <summary>The next free default name after the highest numbered one already in the project.</summary>
    public static string NextDefault(IEnumerable<string>? names)
    {
        var format = Strings.Get("Editor.DefaultMacroName");
        var marker = format.IndexOf("{0}", StringComparison.Ordinal);
        var stem = (marker >= 0 ? format[..marker] : format).TrimEnd();
        var highest = 0L;

        foreach (var name in names ?? [])
        {
            var text = (name ?? string.Empty).Trim();
            if (!text.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var numberText = text[stem.Length..].Trim();
            if (long.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                highest = Math.Max(highest, number);
            }
        }

        return Strings.Format("Editor.DefaultMacroName", highest + 1);
    }

    /// <summary>
    /// A name nobody in the project has: the one asked for when it is free, otherwise the same with
    /// the day it was taken on, otherwise that with a count after it.
    /// </summary>
    /// <remarks>
    /// A macro is reached by its name, so two macros with one name leave the second unreachable: a
    /// call to that name means whichever the table happened to keep. That cannot be reported as a
    /// mistake either — the person did the ordinary thing (imported a package, opened a file
    /// somebody edited) — so the later macro keeps its work under a name of its own.
    /// </remarks>
    public static string Free(string wanted, IEnumerable<string>? taken, DateTime when)
    {
        var name = wanted.Trim();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var other in taken ?? [])
        {
            used.Add((other ?? string.Empty).Trim());
        }

        if (name.Length > 0 && used.Add(name))
        {
            return name;
        }

        var stem = name.Length == 0
            ? when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{name} ({when:yyyy-MM-dd})");
        if (used.Add(stem))
        {
            return stem;
        }

        for (var count = 2; ; count++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{stem} {count}");
            if (used.Add(candidate))
            {
                return candidate;
            }
        }
    }
}
