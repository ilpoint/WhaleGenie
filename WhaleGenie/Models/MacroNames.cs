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
}
