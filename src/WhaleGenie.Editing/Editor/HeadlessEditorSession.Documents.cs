using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>The document's lifecycle outside its steps: the public library, export and save.</summary>

internal sealed partial class HeadlessEditorSession
{
    /// <summary>Writes the page in front to a package of its own, the way the native export did.</summary>
    public static IReadOnlyList<EditorPublicMacroSnapshot> PublicMacros()
    {
        try
        {
            return [.. PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary).Select(entry =>
                new EditorPublicMacroSnapshot(entry.Id, entry.Version,
                    entry.Macro.Name, entry.Macro.Description))];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public EditorSnapshot AddPublicMacro(string id)
    {
        var entry = PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the public library has no macro " + id);
        var macro = MacroItem.FromJson(entry.Macro.ToSubMacroJson());
        var taken = _model.SubMacros.Select(candidate => candidate.Name)
            .Concat(_workspace.Macros.Select(candidate => candidate.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = macro.Name.Trim();
        var name = stem;
        for (var index = 2; taken.Contains(name); index++)
        {
            name = stem + " " + index.ToString(CultureInfo.InvariantCulture);
        }

        macro.Name = name;
        _model.SubMacros.Add(macro);

        var call = new MacroStep
        {
            Type = "control.runMacro",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Written = Formula.Text(name),
                },
            ],
        };

        if (macro.Parameters.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = FieldNames.Arguments,
                Kind = ActionParameterKind.Arguments,
                Rows =
                [
                    .. macro.Parameters.Select(parameter => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Name] = Formula.Text(parameter.Name),
                            [FieldNames.Value] = Formula.Text(string.Empty),
                        },
                    }),
                ],
            });
        }

        if (macro.Results.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = "returns",
                Kind = ActionParameterKind.Returns,
                Rows =
                [
                    .. macro.Results.Select(result => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Key] = Formula.Text(result.Name),
                            [FieldNames.Name] = Formula.Text(result.Name),
                        },
                    }),
                ],
            });
        }

        _model.Steps.Add(call);
        _model.SettleStepIds();
        _model.IsDirty = true;
        return Snapshot();
    }

    public bool Export(string path)
    {
        if (path.Trim().Length == 0)
        {
            throw new InvalidOperationException("an export needs a file name");
        }

        var draft = _model.IsSubMacro
            ? new MacroDraft(_model.BuildMacro(), [], [])
            : _model.Draft();
        var triggers = new ProjectTriggers();
        foreach (var entry in draft.Entries)
        {
            triggers.Entries.Add(entry.Copy());
        }

        foreach (var layout in draft.Layouts)
        {
            triggers.InterfaceLayouts.Add(layout);
        }

        var assets = _workspace.CurrentPath is { Length: > 0 } current
            ? MacroPackage.AssetFolderFor(current)
            : null;
        MacroPackage.Save(path, [draft.Macro],
            MacroBundle.Take(triggers, [draft.Macro]),
            MacroBundle.UsedGlobals([draft.Macro], _workspace.Variables.Globals),
            AppVersion.Text, assets);
        return true;
    }

    public bool Save()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException(SaveProblem is { Length: > 0 } problem
                ? problem
                : Strings.Get("Editor.Problem.CannotSave"));
        }

        var draft = _model.Draft();
        if (_owner is not null)
        {
            _owner.ReplaceSubmacro(_submacroOriginal, draft.Macro);
            _model.IsDirty = false;
            _existing = draft.Macro;
            return true;
        }

        var commit = new ProjectMacroCommit(_workspace, _existing, MacroView.All,
            _ => { }, () => { }, () => { });
        commit.Apply(draft, []);
        CommitGlobals();
        _model.IsDirty = false;
        _existing = _workspace.Macros.FirstOrDefault(candidate => candidate.Name == draft.Macro.Name);
        return _existing is not null;
    }

    private void ReplaceSubmacro(string originalName, MacroItem updated)
    {
        var index = _model.SubMacros.FindIndex(macro =>
            string.Equals(macro.Name, originalName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            _model.SubMacros.Add(updated);
        }
        else
        {
            _model.SubMacros[index] = updated;
        }

        if (_existing is not null)
        {
            var existingIndex = _existing.SubMacros.FindIndex(macro =>
                string.Equals(macro.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                _existing.SubMacros[existingIndex] = updated;
            }
        }

        _model.IsDirty = true;
    }

}
