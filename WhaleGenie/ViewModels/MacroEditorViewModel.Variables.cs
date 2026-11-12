using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// The variables the editor offers and the edits it applies to their names.
public partial class MacroEditorViewModel
{
    /// <summary>
    /// Variables the pickers offer: the ones this macro makes for itself, the shared ones the
    /// Variable Center defines, the ones WhaleGenie provides, and the ending every step leaves
    /// behind — each with what a person choosing one needs to know about it.
    /// </summary>
    public IReadOnlyList<VariableChoice> CollectVariables()
        => VariableChoices.For(Steps, DeclaredParameters, DeclaredResults, DeclaredLocals,
            ReadVariableContent);

    /// <summary>
    /// Why a local may not go by this name, judged against the names the local manager does not own:
    /// a shared value, a built-in, a parameter or a result. Whether two rows of the manager repeat
    /// each other is that window's own business, and it says so itself.
    /// </summary>
    internal string LocalNameConflict(string name)
    {
        var written = name.Trim();
        var taken = CollectVariables().Any(choice =>
            choice.Origin is not (VariableOrigin.Local or VariableOrigin.DeclaredLocal)
            && string.Equals(choice.Name, written, StringComparison.OrdinalIgnoreCase));
        return taken
            ? Strings.Format("Editor.Problem.VariableNameTaken", written)
            : string.Empty;
    }

    /// <summary>Renames a local name this macro owns and all exact references to it.</summary>
    public bool RenameLocalVariable(string oldName, string newName, out string problem)
    {
        var old = oldName.Trim();
        var next = newName.Trim();
        if (!VariableName.IsName(next))
        {
            problem = Strings.Format("Editor.Problem.InvalidVariableName", next);
            return false;
        }

        if (string.Equals(old, next, StringComparison.Ordinal))
        {
            problem = string.Empty;
            return true;
        }

        if (CollectVariables().Any(choice =>
                string.Equals(choice.Name, next, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(choice.Name, old, StringComparison.OrdinalIgnoreCase)))
        {
            problem = Strings.Format("Editor.Problem.VariableNameTaken", next);
            return false;
        }

        RenameLocalEverywhere(old, next);
        NotifyStepsChanged();
        problem = string.Empty;
        return true;
    }

    /// <summary>
    /// Carries one local name through everything this macro can know it by: the fields of the steps,
    /// the other declarations that may start from it, and the declaration itself. No checking — the
    /// callers above decide whether the name may be used, so a batch of renames cannot fail half way
    /// through and leave the macro with one name changed and the rest not.
    /// </summary>
    private void RenameLocalEverywhere(string oldName, string newName)
    {
        var id = DeclaredLocals.FirstOrDefault(local =>
            string.Equals(local.Name.Trim(), oldName, StringComparison.Ordinal))?.Id
            ?? string.Empty;

        VariableRename.RenameSteps(Steps, oldName, newName);
        foreach (var local in DeclaredLocals)
        {
            if (string.Equals(local.Name.Trim(), oldName, StringComparison.Ordinal))
            {
                local.Name = newName;
            }

            local.Initial = VariableRename.RenameValue(local.Initial, oldName, newName);
        }

        foreach (var parameter in DeclaredParameters)
        {
            parameter.Default = VariableRename.RenameValue(parameter.Default, oldName, newName);
        }

        VariableIds.Rebind(Steps, id, newName);
    }

    /// <summary>
    /// Takes what the local variable manager handed back: names that changed, values declared for the
    /// first time, and declarations taken away. One undo entry covers the batch.
    /// </summary>
    /// <returns>Empty when the rows were applied; the reason they were not, otherwise.</returns>
    internal string ApplyLocalVariableEdits(IReadOnlyList<LocalVariableDraft> rows)
    {
        if (InvalidEdit(rows) is { Length: > 0 } refused)
        {
            return refused;
        }

        // Renamed in two passes. One pass would read as a collision half way through whenever two
        // names swap places, because each step of the batch is checked against the names as they
        // stand at that moment; the same name briefly held twice is nobody's business but this
        // method's, so it is written under a name no step can spell.
        var renames = rows
            .Where(row => !row.IsNew
                && !string.Equals(row.OriginalName, row.Name.Trim(), StringComparison.Ordinal))
            .ToList();

        PushUndo();
        var temporary = new List<(string Passed, string Final)>();
        foreach (var row in renames)
        {
            var pass = "__wg" + Guid.NewGuid().ToString("N")[..8];
            RenameLocalEverywhere(row.OriginalName, pass);
            temporary.Add((pass, row.Name.Trim()));
        }

        foreach (var (passed, final) in temporary)
        {
            RenameLocalEverywhere(passed, final);
        }

        var kept = rows
            .Where(row => row.IsDeclared)
            .Select(row => row.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        DeclaredLocals.RemoveAll(local => !kept.Contains(local.Name.Trim()));
        foreach (var row in rows.Where(row => row.IsDeclared))
        {
            var declared = row.ToLocal();
            var existing = DeclaredLocals.FirstOrDefault(local =>
                string.Equals(local.Name.Trim(), declared.Name, StringComparison.Ordinal));
            if (existing is null)
            {
                DeclaredLocals.Add(declared);
                continue;
            }

            existing.Type = declared.Type;
            existing.Initial = declared.Initial;
            existing.Description = declared.Description;
        }

        NotifyStepsChanged();
        MarkDirty();
        return string.Empty;
    }

    /// <summary>The reason a batch cannot be applied, or nothing when it can.</summary>
    private static string InvalidEdit(IReadOnlyList<LocalVariableDraft> rows)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var name = row.Name.Trim();
            if (!VariableName.IsName(name))
            {
                return Strings.Format("Editor.Problem.InvalidVariableName", name);
            }

            if (!seen.Add(name))
            {
                return Strings.Format("Editor.Problem.VariableNameTaken", name);
            }
        }

        return string.Empty;
    }

    /// <summary>Applies a project-level global rename to this editor page's steps and declarations.</summary>
    public void RenameGlobalReferences(string oldName, string newName)
    {
        VariableRename.RenameSteps(Steps, oldName, newName, global: true);
        foreach (var parameter in DeclaredParameters)
        {
            parameter.Default = FormulaText.Rename(parameter.Default, ActionReadMode.WholeValue, oldName, newName);
        }

        NotifyStepsChanged();
    }

    private string? ReadVariableContent(string name, VariableScope scope)
    {
        if (scope is VariableScope.Global)
        {
            if (SharedVariables.Current.TryGet(name, out var value))
            {
                return value.AsText();
            }

            if (Persisted.TryGet(name, out var saved))
            {
                return saved;
            }

            var definition = VariableCatalog.FindGlobal(name);
            return definition is null
                ? null
                : VariableDefinition.Preview(definition.Type, definition.DefaultValue);
        }

        if (scope is VariableScope.System)
        {
            return SystemReader?.Read(name);
        }

        return null;
    }
}
