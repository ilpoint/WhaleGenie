using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Storage;

/// <summary>What to do with a global variable the file and the project both declare.</summary>
public enum GlobalChoice
{
    /// <summary>Keep the project's declaration; the arriving macro reads that one.</summary>
    UseProject,

    /// <summary>Give the arriving one a name of its own and point the arriving macros at it.</summary>
    Rename,

    /// <summary>Replace the project's declaration with the file's.</summary>
    Overwrite,
}

/// <summary>One global variable that is in both places, and what the person decided about it.</summary>
public sealed class GlobalDecision
{
    public required VariableDefinition Arriving { get; init; }

    /// <summary>The declaration the project already has, or null when the name is free.</summary>
    public VariableDefinition? Existing { get; init; }

    public GlobalChoice Choice { get; set; } = GlobalChoice.Rename;

    /// <summary>The name to use when renaming; empty asks for one to be made up.</summary>
    public string NewName { get; set; } = string.Empty;

    public bool HasConflict => Existing is not null;
}

/// <summary>What an import did, in the words the person needs to hear it.</summary>
public sealed record ImportReport(
    IReadOnlyList<MacroItem> Added,
    IReadOnlyList<(string Was, string Now)> RenamedMacros,
    IReadOnlyList<string> GlobalsAdded,
    IReadOnlyList<string> GlobalsRenamed,
    IReadOnlyList<string> GlobalsOverwritten,
    IReadOnlyList<string> GlobalsKept,
    IReadOnlyList<string> DuplicateTriggers,
    IReadOnlyList<string> ScriptsToCheck)
{
    public bool IsEmpty => Added.Count == 0
        && GlobalsAdded.Count == 0
        && GlobalsRenamed.Count == 0;
}

/// <summary>
/// Puts a chosen part of a file into the project, or into the public library.
/// </summary>
/// <remarks>
/// A package is the only file there is, so what a file is meant to become is decided here. Macros
/// that were ticked at the top of the file go into the project as macros; a submacro ticked on its
/// own goes to the public library, because that is where things a macro calls live, and a project
/// receives a copy from there when it wants one.
///
/// Names that are already taken are the ordinary case, not an error: a macro gets the date it
/// arrived on, a global variable gets a number unless the person chose otherwise, and every rename
/// is reported. What must not happen is a silent change of meaning.
/// </remarks>
public static class MacroImport
{
    /// <summary>
    /// One decision for every global variable the chosen macros read and the file declares. A name
    /// the project does not have needs no question: it is added. A name both have is the one the
    /// window asks about, and the one that must not be settled quietly.
    /// </summary>
    public static IReadOnlyList<GlobalDecision> Decisions(IReadOnlyList<MacroItem> chosen,
        IReadOnlyList<VariableDefinition> arriving)
    {
        var used = MacroBundle.UsedGlobals(chosen, arriving)
            .Select(variable => variable.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var decisions = new List<GlobalDecision>();
        foreach (var variable in arriving.Where(variable => used.Contains(variable.Name)))
        {
            var existing = VariableCatalog.Globals.FirstOrDefault(candidate =>
                string.Equals(candidate.Name.Trim(), variable.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase));
            decisions.Add(new GlobalDecision { Arriving = variable, Existing = existing });
        }

        return decisions;
    }

    /// <summary>Adds the chosen macros to the project, bringing the variables they read with them.</summary>
    public static ImportReport IntoProject(MainViewModel project, PackageContents contents,
        IReadOnlyList<MacroItem> chosen, IReadOnlyList<GlobalDecision> decisions)
    {
        var duplicate = new List<string>();
        var seen = project.Triggers.Entries.Select(Signature).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = new List<MacroItem>();
        var renamed = new List<(string Was, string Now)>();

        foreach (var macro in chosen)
        {
            var wanted = macro.Name.Trim();
            project.AddMacro(macro);

            // The macro's own entries came across with it, under whatever name it ended up with. The
            // file's trigger table still says the old one, and the menu merge below reads it, so the
            // rename is carried over there as well.
            if (!string.Equals(wanted, macro.Name, StringComparison.Ordinal))
            {
                contents.Triggers.RenameMacro(wanted, macro.Name);
                renamed.Add((wanted, macro.Name));
            }

            added.Add(macro);
            foreach (var entry in macro.Triggers.Where(entry => seen.Contains(Signature(entry))))
            {
                duplicate.Add($"{entry.KindLabel} · {macro.Name} · {entry.Summary}");
            }

            foreach (var entry in macro.Triggers)
            {
                seen.Add(Signature(entry));
            }
        }

        project.Triggers.MergeMenuFrom(contents.Triggers, added);

        var globals = Apply(added, decisions);
        return new ImportReport(added, renamed, globals.Added, globals.Renamed, globals.Overwritten,
            globals.Kept, duplicate, globals.Scripts);
    }

    /// <summary>
    /// Adds the chosen submacros to the public library, each as an entry of its own.
    /// </summary>
    public static IReadOnlyList<PublicMacroEntry> IntoLibrary(IReadOnlyList<MacroItem> chosen,
        string library)
    {
        var entries = PublicMacroLibrary.Load(library).ToList();
        var names = entries.Select(entry => entry.Macro.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = new List<PublicMacroEntry>();
        foreach (var macro in chosen)
        {
            var copy = MacroItem.FromJson(macro.ToSubMacroJson());
            copy.Name = FreeName(macro.Name.Trim(), names.Contains);
            copy.Origin = null;

            var entry = new PublicMacroEntry(Guid.NewGuid().ToString("N"), 1, copy);
            entries.Add(entry);
            added.Add(entry);
            names.Add(copy.Name);
        }

        PublicMacroLibrary.Save(library, entries, AppVersion.Text);
        return added;
    }

    /// <summary>
    /// A macro from the file as something that can stand on its own: a submacro ticked on its own
    /// leaves as a macro, and is written into a file the same way it is put into a project.
    /// </summary>
    public static MacroItem Promoted(MacroItem sub) => MacroItem.FromJson(sub.ToSubMacroJson());

    private static (List<string> Added, List<string> Renamed, List<string> Overwritten,
        List<string> Kept, List<string> Scripts) Apply(IReadOnlyList<MacroItem> added,
        IReadOnlyList<GlobalDecision> decisions)
    {
        var addedNames = new List<string>();
        var renamed = new List<string>();
        var overwritten = new List<string>();
        var kept = new List<string>();
        var scripts = new List<string>();

        var taken = VariableCatalog.Globals.Select(variable => variable.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var decision in decisions)
        {
            var name = decision.Arriving.Name.Trim();

            if (decision.Existing is not { } existing)
            {
                VariableCatalog.Globals.Add(Clone(decision.Arriving, name));
                taken.Add(name);
                addedNames.Add(name);
                continue;
            }

            switch (decision.Choice)
            {
                case GlobalChoice.UseProject:
                    kept.Add(name);
                    break;

                case GlobalChoice.Overwrite:
                    existing.Type = decision.Arriving.Type;
                    existing.DefaultValue = decision.Arriving.DefaultValue;
                    existing.Description = decision.Arriving.Description;
                    existing.PersistValue = decision.Arriving.PersistValue;
                    overwritten.Add(name);
                    break;

                default:
                    var target = FreeName(decision.NewName.Trim(),
                        candidate => taken.Contains(candidate)
                            || VariableRename.GlobalConflict(added, candidate) is not null);
                    foreach (var macro in VariableRename.ScriptUsers(added, name))
                    {
                        scripts.Add($"{macro} · {name}");
                    }

                    VariableRename.RenameGlobal(added, name, target);
                    VariableCatalog.Globals.Add(Clone(decision.Arriving, target));
                    taken.Add(target);
                    renamed.Add($"{name} → {target}");
                    break;
            }
        }

        return (addedNames, renamed, overwritten, kept, scripts);
    }

    private static VariableDefinition Clone(VariableDefinition source, string name) => new()
    {
        Name = name,
        Scope = VariableScope.Global,
        Type = source.Type,
        DefaultValue = source.DefaultValue,
        Description = source.Description,
        PersistValue = source.PersistValue,
    };

    /// <summary>
    /// A variable name nothing in the project answers to. Numbered from two when the name is taken,
    /// and cut down to what a variable may be called: a name arriving from a file may hold spaces
    /// or brackets that the variable center would refuse to save.
    /// </summary>
    private static string FreeName(string wanted, Func<string, bool> taken)
    {
        var stem = new string([.. wanted.Where(character =>
            char.IsLetterOrDigit(character) || character is '_' or '.')]);
        if (stem.Length == 0)
        {
            stem = Strings.Get("Import.FallbackVariable");
        }

        if (!taken(stem))
        {
            return stem;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{stem}{index}";
            if (!taken(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// What makes two trigger entries the same question: the kind of thing being watched and the
    /// condition it is watched for. The macro they start is left out on purpose — two macros on one
    /// hotkey is exactly the collision worth reporting.
    /// </summary>
    private static string Signature(TriggerEntry entry) => $"{entry.Kind}|{entry.Summary}";
}
