using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>
/// One action being written, on top of the same AddActionViewModel the native dialog used. The page
/// edits this form through commands; the step only enters the tree when it is confirmed, and the
/// draft and the saved step never exist as two copies of the same fields.
/// </summary>
internal sealed partial class HeadlessActionSession
{
    private readonly AddActionViewModel _form;
    private readonly MacroStep? _original;
    private readonly Func<StepParameterViewModel, EditorWindowPreviewSnapshot?> _preview;

    public HeadlessActionSession(AddActionViewModel form, MacroStep? original)
    {
        _form = form;
        _original = original;
        _preview = Preview;
    }

    public AddActionViewModel Form => _form;

    public bool IsEditing => _original is not null;

    public MacroStep? Original => _original;

    public string StepId => _form.StepId;

    public EditorActionFormSnapshot Snapshot()
        => EditorActionSnapshotFactory.Build(_form, _form.StepId,
            _original?.Id ?? string.Empty, _form.JsonPreview, _preview);

    public MacroStep BuildStepForSave() => _form.BuildStepForSave();

    public EditorActionFormSnapshot SetPage(string page)
    {
        var wanted = _form.Pages.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, page, StringComparison.Ordinal));
        if (wanted is not null)
        {
            _form.Select(wanted);
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot PatchField(string name, string field, string value)
    {
        var parameter = Field(name);
        switch (field)
        {
            case "text":
                parameter.WriteValue(Formula.Text(value));
                break;
            case "name":
                parameter.WriteValue(Formula.Name(value.Trim()));
                break;
            case "number":
                if (decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture,
                        out var number))
                {
                    parameter.SetNumber((int)decimal.Round(number));
                }
                else
                {
                    parameter.WriteValue(Formula.Nothing);
                }

                break;
            case "bool":
                parameter.Flag = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "choice":
                parameter.Choose(value);
                break;
            case "macro":
                parameter.WriteValue(Formula.Text(value.Trim()));
                break;
            case "key":
                parameter.WriteKey(value.Trim());
                break;
            default:
                throw new InvalidOperationException("unknown field edit " + field);
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot AddRow(string name)
    {
        var parameter = Field(name);
        switch (parameter.Definition.Kind)
        {
            case ActionParameterKind.Region:
                parameter.AddRegion();
                break;
            case ActionParameterKind.Images:
                parameter.AddPicture();
                break;
            case ActionParameterKind.ColourPoints:
                parameter.AddColourPoint();
                break;
            case ActionParameterKind.Color when parameter.IsColourRows:
                parameter.AddColourRow();
                break;
            case ActionParameterKind.KeySequence:
                parameter.AddKeyRow();
                break;
            case ActionParameterKind.Narrow:
                parameter.AddNarrowRow();
                break;
            default:
                throw new InvalidOperationException("the field has no rows to add to");
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot RemoveRow(string name, int index)
    {
        var parameter = Field(name);
        switch (parameter.Definition.Kind)
        {
            case ActionParameterKind.Region:
                parameter.RemoveRegion(At(parameter.Regions, index));
                break;
            case ActionParameterKind.Images:
                parameter.RemovePicture(At(parameter.Pictures, index));
                break;
            case ActionParameterKind.ColourPoints:
                parameter.RemoveColourPoint(At(parameter.ColourPointRows, index));
                break;
            case ActionParameterKind.Color when parameter.IsColourRows:
                parameter.RemoveColourRow(At(parameter.ColourRows, index));
                break;
            case ActionParameterKind.KeySequence:
                parameter.RemoveKeyRow(At(parameter.KeyRows, index));
                break;
            case ActionParameterKind.Narrow:
                parameter.RemoveNarrowRow(At(parameter.NarrowRows, index));
                break;
            default:
                throw new InvalidOperationException("the field has no rows to remove");
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot MoveRow(string name, int index, int by)
    {
        var parameter = Field(name);
        if (parameter.Definition.Kind is not ActionParameterKind.Images)
        {
            throw new InvalidOperationException("only a list of pictures can be reordered");
        }

        parameter.MovePicture(At(parameter.Pictures, index), by);
        return Snapshot();
    }

    public EditorActionFormSnapshot PatchRow(string name, int index, string column, string value)
    {
        var parameter = Field(name);
        switch (parameter.Definition.Kind)
        {
            case ActionParameterKind.Region:
                {
                    var row = At(parameter.Regions, index);
                    switch (column)
                    {
                        case "x": row.X.Text = value; break;
                        case "y": row.Y.Text = value; break;
                        case "width": row.Width.Text = value; break;
                        case "height": row.Height.Text = value; break;
                        case "variable": row.OrVariable = value; break;
                        default: throw new InvalidOperationException("unknown region column " + column);
                    }

                    break;
                }
            case ActionParameterKind.Images:
                At(parameter.Pictures, index).Text = value;
                break;
            case ActionParameterKind.ColourPoints:
                {
                    var row = At(parameter.ColourPointRows, index);
                    switch (column)
                    {
                        case "dx": row.Dx = value; break;
                        case "dy": row.Dy = value; break;
                        case "colour": row.Colour = value; break;
                        case "mustNot": row.MustNot = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase); break;
                        case "tolerance": row.Tolerance = value; break;
                        default: throw new InvalidOperationException("unknown colour column " + column);
                    }

                    break;
                }
            case ActionParameterKind.Color when parameter.IsColourRows:
                {
                    var row = At(parameter.ColourRows, index);
                    if (column == "colour")
                    {
                        row.Colour = value;
                    }
                    else if (column == "tolerance")
                    {
                        row.Tolerance = value;
                    }
                    else
                    {
                        throw new InvalidOperationException("unknown colour column " + column);
                    }

                    break;
                }
            case ActionParameterKind.KeySequence:
                {
                    var row = At(parameter.KeyRows, index);
                    switch (column)
                    {
                        case "keys": row.Keys = value; break;
                        case "hold": row.HoldMs = Number(value); break;
                        case "gap": row.GapMs = Number(value); break;
                        default: throw new InvalidOperationException("unknown key column " + column);
                    }

                    break;
                }
            case ActionParameterKind.Narrow:
                {
                    var row = At(parameter.NarrowRows, index);
                    if (column == "value")
                    {
                        row.Value = value;
                    }
                    else if (column == "how")
                    {
                        row.Way = row.Choices.FirstOrDefault(way => way.Value == value) ?? row.Way;
                    }
                    else
                    {
                        throw new InvalidOperationException("unknown narrow column " + column);
                    }

                    break;
                }
            default:
                throw new InvalidOperationException("the field has no rows to edit");
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot PatchReturn(string key, bool? ticked, string? name)
    {
        var row = Return(key);
        if (ticked is { } value)
        {
            row.Ticked = value;
        }

        if (name is not null)
        {
            row.Load(name);
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot FixReturnName(string key)
    {
        Return(key).MakeNameUnique();
        return Snapshot();
    }

    public EditorActionFormSnapshot PatchCallArgument(string name, bool? given, string? value)
    {
        var row = Argument(name);
        if (given is { } givenValue)
        {
            row.Given = givenValue;
        }

        if (value is not null)
        {
            row.Value = value;
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot PatchMeta(string field, string value, string unit)
    {
        switch (field)
        {
            case "enabled":
                _form.MetaEnabled = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "comment":
                _form.MetaComment = value;
                break;
            case "timeoutMs":
                _form.MetaTimeout.Load(Whole(value));
                SetUnit(_form.MetaTimeout, unit);
                break;
            case "retryCount":
                _form.MetaRetryCount = decimal.TryParse(value.Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var retries)
                    ? retries
                    : null;
                break;
            case "retryDelayMs":
                _form.MetaRetryDelay.Load(Whole(value));
                SetUnit(_form.MetaRetryDelay, unit);
                break;
            case "delayBeforeMs":
                _form.MetaDelayBefore.Load(Whole(value));
                SetUnit(_form.MetaDelayBefore, unit);
                break;
            case "delayAfterMs":
                _form.MetaDelayAfter.Load(Whole(value));
                SetUnit(_form.MetaDelayAfter, unit);
                break;
            case "retryBackoff":
                _form.MetaRetryBackoff = _form.BackoffChoices.First(choice => choice.Value == value);
                break;
            case "onError":
                _form.MetaOnError = _form.ErrorChoices.First(choice => choice.Value == value);
                break;
            case "unit":
                SetUnit(_form.MetaTimeout, value);
                break;
            default:
                throw new InvalidOperationException("unknown step setting " + field);
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot PatchGlobal(string field, string value)
    {
        if (_form.GlobalVariableDraft is not { } draft)
        {
            throw new InvalidOperationException("this action carries no global declaration");
        }

        switch (field)
        {
            case "name":
                draft.Name = value;
                break;
            case "type":
                draft.Type = Enum.TryParse<ValueKind>(value, ignoreCase: true, out var type)
                    ? type
                    : draft.Type;
                break;
            case "default":
                draft.Default = Formula.Text(value);
                break;
            case "description":
                draft.Description = value;
                break;
            case "persist":
                draft.PersistValue = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            default:
                throw new InvalidOperationException("unknown global setting " + field);
        }

        return Snapshot();
    }

    public EditorActionFormSnapshot AddBranch()
    {
        _form.AddBranchCommand.Execute(null);
        return Snapshot();
    }

    public EditorActionFormSnapshot PatchWindow(string name, string choice, string value)
    {
        var field = Field(name);
        switch (choice)
        {
            case "foreground":
                field.ClearWindowRule();
                break;
            case "follow":
                field.FollowWindowRule();
                break;
            case "clear":
                field.ClearWindowRule();
                break;
            case "variable":
                field.UseWindowVariable(value);
                break;
            case "rule":
            case "open":
            case "capture":
                if (!WindowRule.TryParse(value, out var rule))
                {
                    throw new InvalidOperationException("the window rule cannot be read");
                }

                field.ApplyWindowRule(rule);
                break;
            default:
                throw new InvalidOperationException("unknown window choice " + choice);
        }

        return Snapshot();
    }

    public IReadOnlyList<EditorWindowEntrySnapshot> WindowList()
    {
        if (_form.Windows is null)
        {
            return [];
        }

        var own = System.IO.Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? string.Empty);
        try
        {
            return [.. _form.Windows.List()
                .Where(window => !string.Equals(_form.Windows.ProcessOf(window.Handle), own,
                    StringComparison.OrdinalIgnoreCase))
                .Select(window =>
                {
                    var process = string.Empty;
                    var className = string.Empty;
                    try
                    {
                        process = _form.Windows.ProcessOf(window.Handle);
                        className = _form.Windows.ClassOf(window.Handle);
                    }
                    catch (Exception)
                    {
                        // A window that went away still has its title; the rule falls back to it.
                    }

                    var rule = WindowRule.From(window, process, className);
                    return new EditorWindowEntrySnapshot(window.Handle, window.Title, process, className,
                        rule.MatchValue, rule.ToJson().ToJsonString());
                })];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public EditorActionFormSnapshot ApplyWindowRule(string name, string ruleJson)
    {
        if (!WindowRule.TryParse(ruleJson, out var rule))
        {
            throw new InvalidOperationException("the window rule cannot be read");
        }

        Field(name).ApplyWindowRule(rule);
        return Snapshot();
    }

    public EditorActionFormSnapshot Say(string message, bool warning)
    {
        _form.Say(message, warning);
        return Snapshot();
    }

    private StepParameterViewModel Field(string name)
        => _form.Parameters.FirstOrDefault(parameter =>
               string.Equals(parameter.Definition.Name, name, StringComparison.Ordinal))
           ?? throw new InvalidOperationException("the action has no field " + name);

    private ReturnsRowViewModel Return(string key)
        => _form.ReturnsRows.FirstOrDefault(row =>
               string.Equals(row.Key, key, StringComparison.Ordinal))
           ?? throw new InvalidOperationException("the action has no result " + key);

    private CallArgumentRowViewModel Argument(string name)
        => _form.CallArgumentRows.FirstOrDefault(row =>
               string.Equals(row.Name, name, StringComparison.OrdinalIgnoreCase))
           ?? throw new InvalidOperationException("the call has no argument " + name);

    private static T At<T>(IReadOnlyList<T> rows, int index)
        => index >= 0 && index < rows.Count
            ? rows[index]
            : throw new InvalidOperationException("the list has no row " + index);

    private static int Whole(string value)
        => int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? Math.Max(0, number)
            : 0;

    private static decimal? Number(string value)
        => decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static void SetUnit(DurationSettingViewModel setting, string unit)
    {
        var wanted = setting.Units.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, unit, StringComparison.Ordinal));
        if (wanted is not null)
        {
            setting.Unit = wanted;
        }
    }

    private EditorWindowPreviewSnapshot? Preview(StepParameterViewModel field)
    {
        if (!field.IsWindow || _form.Windows is null)
        {
            return null;
        }

        var preview = new WindowPreviewViewModel();
        try
        {
            preview.Watch(() => _form.Windows);
            preview.Follow(_form.WindowRuleFor(field.Definition.Name),
                emptyIsEvery: _form.SelectedKey == "window.list",
                needsAnswer: field.HasToBeAnswered,
                holdsVariable: field.CurrentShape is VariableNode);
            return new EditorWindowPreviewSnapshot(preview.Summary, preview.Note, preview.More,
                [.. preview.Rows.Select(row => new EditorWindowPreviewRowSnapshot(
                    row.Title, row.PartLabel, row.Part))]);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            preview.Stop();
        }
    }
}
