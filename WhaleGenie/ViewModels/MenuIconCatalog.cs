using System;
using System.Collections.Generic;
using System.Linq;
using Lucide.Avalonia;

namespace WhaleGenie.ViewModels;

/// <summary>One selectable Lucide icon. Its key is the stable enum name saved in the project.</summary>
public sealed record MenuIconOption(LucideIconKind Kind)
{
    public string Key => Kind.ToString();

    public string Label => Key;
}

/// <summary>
/// Every icon contained in the Lucide package, labelled by its official name so the file value,
/// what the picker shows and what search looks through are one thing.
/// </summary>
public static class MenuIconCatalog
{
    public static IReadOnlyList<MenuIconOption> Options { get; } =
        [.. Enum.GetValues<LucideIconKind>()
            .Select(kind => new MenuIconOption(kind))
            .OrderBy(option => option.Key, StringComparer.Ordinal)];

    private static readonly IReadOnlyDictionary<string, MenuIconOption> ByKey =
        Options.ToDictionary(option => option.Key, StringComparer.Ordinal);

    public static MenuIconOption? Find(string? key)
        => key is not null && ByKey.TryGetValue(key, out var option) ? option : null;

    /// <summary>
    /// Whether a key names an icon this build has. The model asks through this rather than holding
    /// the catalogue itself: which artwork exists is the interface's fact, and the project file only
    /// remembers the name.
    /// </summary>
    public static bool Knows(string? key) => Find(key) is not null;
}
