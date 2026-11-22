using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

// What the page needs to draw one action being written or edited. The facts are read off the same
// AddActionViewModel and StepParameterViewModel the native dialog used, so a field's kind, the page
// it belongs to and the picker beside it are decided once in C#, not guessed at in TypeScript.
internal sealed record EditorActionFormSnapshot(
    bool Open, bool IsEditing, bool CanConfirm, bool CanRun, bool Running, string RunLabel,
    string ValidationMessage, bool ValidationIsProblem,
    string ActionKey, string Name, string Description, string StepId, string CommitLabel,
    string Page, IReadOnlyList<EditorActionPageSnapshot> Pages,
    IReadOnlyList<EditorActionRowSnapshot> Rows,
    IReadOnlyList<EditorActionRowSnapshot> AdvancedRows,
    IReadOnlyList<EditorReturnsRowSnapshot> Returns,
    IReadOnlyList<EditorCallArgumentSnapshot> CallArguments,
    EditorSteppingMetaSnapshot Meta, EditorGlobalDraftSnapshot? GlobalDraft,
    IReadOnlyList<EditorWindowRuleChoiceSnapshot> WindowRules, string WindowAbove,
    string BranchSummary, string AttemptSummary, bool IsBlock, bool ShowsBranches, bool ShowsAttempt,
    string Json, string Notice, bool NoticeIsWarning, EditorClipboardPreviewSnapshot ClipboardPreview);

internal sealed record EditorActionRowSnapshot(
    string GroupKey, string GroupTitle, string GroupHint, int GroupIndex, bool IsGroupStart,
    bool IsQuad, IReadOnlyList<EditorActionFieldSnapshot> Cells, EditorPickerSnapshot? Picker);

internal sealed record EditorPickerSnapshot(string Kind, string Label, string Hint, string Mode);

internal sealed record EditorActionFieldSnapshot(
    string Name, string Label, string Hint, string Kind, string ValueForm, string Value, string Display,
    IReadOnlyList<EditorValuePartSnapshot> Parts, IReadOnlyList<EditorChoiceSnapshot> Options,
    IReadOnlyList<EditorColumnSnapshot> Columns, EditorConditionSnapshot? Condition,
    bool IsApplicable, bool IsDefault, bool IsMissing, bool Required, bool Advanced,
    string Group, string GroupLabel, bool OffersValue, bool TakesFormula, bool ShowsNameMark,
    bool AllowsNewName, bool IsNumber, bool IsNumberFormula, bool IsDuration, string Unit,
    string UnitLabel, IReadOnlyList<EditorChoiceSnapshot> DurationUnits, bool IsPercent,
    double PercentValue, string ChannelNote, string JitterRange, bool IsMultiline, bool IsCode,
    bool IsColor, string Colour, bool IsPath, string PathIntent, string PathFilter,
    bool IsKey, bool IsKeys, bool IsSelector, bool IsBrowserTarget, bool IsImage, bool IsImageList,
    bool IsRegion, bool IsRegionList, bool IsColourPoints, bool IsColourRows, bool IsKeySequence,
    bool IsNarrow, bool IsWindow, bool IsWindowRule, bool IsMacro, bool IsStepChoice, bool IsRecord,
    bool IsPoint, bool IsRect, string Placeholder, bool HasToBeAnswered, bool IsFollowingWindow,
    string WindowRuleSummary, IReadOnlyList<EditorRegionRowSnapshot> Regions,
    IReadOnlyList<EditorImageRowSnapshot> Images, IReadOnlyList<EditorColourRowSnapshot> Colours,
    IReadOnlyList<EditorColourPointRowSnapshot> ColourPoints, IReadOnlyList<EditorKeyRowSnapshot> Keys,
    IReadOnlyList<EditorNarrowRowSnapshot> NarrowRows, IReadOnlyList<EditorGenericRowSnapshot> Rows,
    IReadOnlyList<EditorChoiceSnapshot> Macros, IReadOnlyList<EditorChoiceSnapshot> Steps,
    IReadOnlyList<EditorBrowserCandidateSnapshot> Candidates,
    EditorWindowPreviewSnapshot? WindowPreview, EditorImagePreviewSnapshot? ImagePreview);

internal sealed record EditorRegionRowSnapshot(string X, string Y, string Width, string Height,
    string Variable);
internal sealed record EditorImageRowSnapshot(string Text, bool CanMoveUp, bool CanMoveDown,
    EditorImagePreviewSnapshot? Preview);

internal sealed record EditorImagePreviewSnapshot(string DataUrl, string FileName, string Problem);
internal sealed record EditorColourRowSnapshot(string Colour, string Tolerance);
internal sealed record EditorColourPointRowSnapshot(string Dx, string Dy, string Colour, bool MustNot,
    string Tolerance);
internal sealed record EditorKeyRowSnapshot(int Number, string Keys, string Hold, string Gap);
internal sealed record EditorNarrowRowSnapshot(string How, string Value, string Count, bool NeedsValue,
    bool WantsWords, bool OnPage, IReadOnlyList<EditorChoiceSnapshot> Ways);
internal sealed record EditorGenericRowSnapshot(IReadOnlyList<string> Cells);

internal sealed record EditorReturnsRowSnapshot(string Key, string Label, string Hint, string Structure,
    string StructureTip, bool Ticked, string Name, bool IsNameTaken, string NameWarning, bool IsPrimary);

internal sealed record EditorCallArgumentSnapshot(string Name, string Hint, bool Required, bool Declared,
    bool Given, string Value);

internal sealed record EditorSteppingMetaSnapshot(bool Enabled, string Comment,
    EditorDurationSnapshot Timeout, decimal? RetryCount, EditorDurationSnapshot RetryDelay,
    EditorDurationSnapshot DelayBefore, EditorDurationSnapshot DelayAfter, string RetryBackoff,
    string OnError, string RetryPlan, bool Tuned);

internal sealed record EditorDurationSnapshot(int Milliseconds, string Written, string Unit,
    IReadOnlyList<EditorChoiceSnapshot> Units);

internal sealed record EditorGlobalDraftSnapshot(string Name, string Type, string Default,
    string DefaultPreview, string Description, bool PersistValue, string Problem, bool HasError,
    IReadOnlyList<EditorChoiceSnapshot> Types);

internal sealed record EditorWindowRuleChoiceSnapshot(string Summary, string Rule, int Count);

internal sealed record EditorWindowPreviewSnapshot(string Summary, string Note, string More,
    IReadOnlyList<EditorWindowPreviewRowSnapshot> Rows);

internal sealed record EditorWindowPreviewRowSnapshot(string Title, string PartLabel, string Part);

internal sealed record EditorBrowserCandidateSnapshot(string Selector, string Text);

internal sealed record EditorClipboardPreviewSnapshot(bool Shown, string Note);

/// <summary>Projects one AddActionViewModel into the page's shape. Pure reads; no editing here.</summary>
internal static class EditorActionSnapshotFactory
{
    public static EditorActionFormSnapshot Build(AddActionViewModel form, string stepId, string originalStepId,
        string json, Func<StepParameterViewModel, EditorWindowPreviewSnapshot?>? windowPreview = null,
        Func<string, EditorImagePreviewSnapshot?>? imagePreview = null)
    {
        var returns = form.ReturnsRows
            .Select(row => new EditorReturnsRowSnapshot(row.Key, row.Label, row.Hint, row.Structure,
                row.StructureTip, row.Ticked, row.Name, row.IsNameTaken, row.NameWarning, row.IsPrimary))
            .ToList();
        var arguments = form.CallArgumentRows
            .Select(row => new EditorCallArgumentSnapshot(row.Name, row.Hint, row.IsRequired,
                row.IsDeclared, row.Given, row.Value))
            .ToList();
        var global = form.GlobalVariableDraft is { } draft
            ? new EditorGlobalDraftSnapshot(draft.Name, draft.Type.ToString(), draft.DefaultValue,
                draft.DefaultPreview, draft.Description, draft.PersistValue, draft.DefaultProblem,
                draft.DefaultHasError, Choices(draft.TypeOptions))
            : null;
        return new EditorActionFormSnapshot(
            Open: true,
            IsEditing: form.IsEditing,
            CanConfirm: form.CanSave,
            CanRun: form.CanRun,
            Running: form.IsRunning,
            RunLabel: form.RunLabel,
            ValidationMessage: form.ValidationMessage,
            ValidationIsProblem: form.ValidationIsProblem,
            ActionKey: form.SelectedKey,
            Name: form.SelectedActionName,
            Description: form.Description,
            StepId: stepId,
            CommitLabel: form.CommitLabel,
            Page: form.Page?.Key ?? AddActionViewModel.BasePage,
            Pages: [.. form.Pages.Select(page => new EditorActionPageSnapshot(page.Key, page.Title,
                page.IsPresent, page.HasDot))],
            Rows: [.. form.Rows.Select(row => Row(row, windowPreview, imagePreview))],
            AdvancedRows: [.. form.AdvancedRows.Select(row => Row(row, windowPreview, imagePreview))],
            Returns: returns,
            CallArguments: arguments,
            Meta: Meta(form),
            GlobalDraft: global,
            WindowRules: [.. form.WindowRules.Select(choice =>
                new EditorWindowRuleChoiceSnapshot(choice.Summary, choice.Rule, choice.Count))],
            WindowAbove: form.WindowAbove,
            BranchSummary: form.ShowsBranches ? form.BranchSummary : string.Empty,
            AttemptSummary: form.ShowsAttempt ? form.AttemptSummary : string.Empty,
            IsBlock: form.IsBlock,
            ShowsBranches: form.ShowsBranches,
            ShowsAttempt: form.ShowsAttempt,
            Json: json,
            Notice: form.Notice,
            NoticeIsWarning: form.NoticeIsWarning,
            ClipboardPreview: new EditorClipboardPreviewSnapshot(form.ShowsClipboardPreview,
                form.ClipboardPreviewNote));
    }

    public static EditorActionFormSnapshot Closed()
        => new(false, false, false, false, false, string.Empty, string.Empty, false,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            [], [], [], [], [], new EditorSteppingMetaSnapshot(false, string.Empty, Duration(0, null, []),
                null, Duration(0, null, []), Duration(0, null, []), Duration(0, null, []), "fixed", "stop",
                string.Empty, false), null, [], string.Empty, string.Empty, string.Empty, false, false,
            false, string.Empty, string.Empty, false, new EditorClipboardPreviewSnapshot(false, string.Empty));

    private static EditorActionRowSnapshot Row(ParameterRowViewModel row,
        Func<StepParameterViewModel, EditorWindowPreviewSnapshot?>? windowPreview,
        Func<string, EditorImagePreviewSnapshot?>? imagePreview)
    {
        var first = row.First;
        return new EditorActionRowSnapshot(row.GroupKey, row.GroupTitle, row.GroupHint, row.GroupIndex,
            row.IsGroupStart, row.IsQuad,
            [.. new[] { row.First, row.Second, row.Third, row.Fourth }
                .Where(field => field is not null)
                .Select(field => Field(field!, windowPreview, imagePreview))],
            Picker(row));
    }

    private static EditorPickerSnapshot? Picker(ParameterRowViewModel row)
    {
        if (row.IsRegionAnchor)
        {
            var drag = row.First.Definition.OwnerKey == "input.mouseDrag";
            return new EditorPickerSnapshot("Region", row.RegionPickerLabel, row.RegionPickerHint,
                drag ? "drag" : "region");
        }

        if (row.IsPointAnchor)
        {
            return new EditorPickerSnapshot("Point", Strings.Get("Add.PickCoordinate"),
                Strings.Get("Add.PickCoordinateHint"), "point");
        }

        if (row.IsWindowBoundsAnchor)
        {
            return new EditorPickerSnapshot("WindowBounds", Strings.Get("Add.PickWindowBounds"),
                Strings.Get("Add.PickWindowBoundsHint"), "windowBounds");
        }

        if (row.ShowsOffsetPick)
        {
            return new EditorPickerSnapshot("LookOffset", Strings.Get("Add.PickOffset"),
                Strings.Get("Add.PickOffsetHint"), "lookOffset");
        }

        return null;
    }

    private static EditorActionFieldSnapshot Field(StepParameterViewModel field,
        Func<StepParameterViewModel, EditorWindowPreviewSnapshot?>? windowPreview,
        Func<string, EditorImagePreviewSnapshot?>? imagePreview)
        => new(
            Name: field.Definition.Name,
            Label: field.Label,
            Hint: field.Hint,
            Kind: field.Definition.Kind.ToString(),
            ValueForm: field.Definition.ValueForm.ToString(),
            Value: field.CurrentText,
            Display: field.DisplayLine,
            Parts: Parts(field),
            Options: Choices(field.Choices),
            Columns: [.. field.Definition.Semantics.Columns.Select(column =>
                new EditorColumnSnapshot(column.Name, column.Required))],
            Condition: Condition(field),
            IsApplicable: field.IsApplicable,
            IsDefault: field.IsDefault,
            IsMissing: field.IsMissing,
            Required: field.Definition.Required,
            Advanced: field.IsAdvanced,
            Group: field.Group,
            GroupLabel: field.Group.Length > 0 ? Strings.Get("Add.Group." + field.Group) : string.Empty,
            OffersValue: field.OffersValueWindow,
            TakesFormula: field.TakesFormula,
            ShowsNameMark: field.ShowsNameMark,
            AllowsNewName: field.AllowsNewName,
            IsNumber: field.IsNumber,
            IsNumberFormula: field.IsNumberFormula,
            IsDuration: field.IsDuration,
            Unit: field.Definition.Semantics.Unit?.ToString() ?? string.Empty,
            UnitLabel: field.Definition.Semantics.Unit is null ? string.Empty : field.Unit.Label,
            DurationUnits: [.. field.DurationUnits.Select(unit => new EditorChoiceSnapshot(
                unit.Key, unit.Label))],
            IsPercent: field.IsPercent,
            PercentValue: field.PercentValue,
            ChannelNote: field.ChannelNote,
            JitterRange: field.JitterRange,
            IsMultiline: field.IsMultiline,
            IsCode: field.IsCode,
            IsColor: field.IsColor,
            Colour: field.CurrentText,
            IsPath: field.IsPath,
            PathIntent: field.Definition.PathIntent.ToString(),
            PathFilter: field.Definition.PathFilter,
            IsKey: field.IsKey,
            IsKeys: field.IsKeys,
            IsSelector: field.IsSelector,
            IsBrowserTarget: field.IsBrowserTarget,
            IsImage: field.IsImage,
            IsImageList: field.IsImageList,
            IsRegion: field.IsRegion,
            IsRegionList: field.Definition.Kind is ActionParameterKind.Region,
            IsColourPoints: field.IsColourPoints,
            IsColourRows: field.IsColourRows,
            IsKeySequence: field.IsKeySequence,
            IsNarrow: field.IsNarrow,
            IsWindow: field.IsWindow,
            IsWindowRule: field.IsWindowRule,
            IsMacro: field.Definition.Kind is ActionParameterKind.Macro,
            IsStepChoice: field.IsStepChoice,
            IsRecord: field.Definition.Kind is ActionParameterKind.Record,
            IsPoint: field.Definition.Kind is ActionParameterKind.Point,
            IsRect: field.Definition.Kind is ActionParameterKind.Rect,
            Placeholder: field.Placeholder,
            HasToBeAnswered: field.HasToBeAnswered,
            IsFollowingWindow: field.IsFollowingWindow,
            WindowRuleSummary: field.IsWindowRule ? field.WindowRuleSummary : string.Empty,
            Regions: [.. field.Regions.Select(row => new EditorRegionRowSnapshot(
                row.X.Text, row.Y.Text, row.Width.Text, row.Height.Text, row.OrVariable))],
            Images: [.. field.Pictures.Select((row, index) => new EditorImageRowSnapshot(
                row.Text, index > 0, index < field.Pictures.Count - 1,
                imagePreview?.Invoke(row.Text)))],
            Colours: [.. field.ColourRows.Select(row => new EditorColourRowSnapshot(
                row.Colour, row.Tolerance))],
            ColourPoints: [.. field.ColourPointRows.Select(row => new EditorColourPointRowSnapshot(
                row.Dx, row.Dy, row.Colour, row.MustNot, row.Tolerance))],
            Keys: [.. field.KeyRows.Select(row => new EditorKeyRowSnapshot(
                row.Number, row.Keys, Number(row.HoldMs), Number(row.GapMs)))],
            NarrowRows: [.. field.NarrowRows.Select(row => new EditorNarrowRowSnapshot(
                row.Way.Value, row.Value, row.Count, row.NeedsValue, row.Way.WantsWords, row.OnPage,
                [.. row.Choices.Select(way => new EditorChoiceSnapshot(way.Value, way.Display))]))],
            Rows: [.. GenericRows(field)],
            Macros: [.. field.Macros.Select(name => new EditorChoiceSnapshot(name, name))],
            Steps: [.. field.Steps.Select(step => new EditorChoiceSnapshot(step.Value, step.Display))],
            Candidates: [.. field.Candidates.Select(candidate =>
                new EditorBrowserCandidateSnapshot(candidate.Selector, candidate.Text))],
            WindowPreview: windowPreview?.Invoke(field),
            ImagePreview: field.IsImage || field.IsPath
                ? imagePreview?.Invoke(field.CurrentText) : null);

    private static IEnumerable<EditorGenericRowSnapshot> GenericRows(StepParameterViewModel field)
    {
        if (field.Definition.Kind is ActionParameterKind.Region or ActionParameterKind.ColourPoints
            or ActionParameterKind.KeySequence or ActionParameterKind.Images
            or ActionParameterKind.Returns or ActionParameterKind.Narrow or ActionParameterKind.Arguments
            || field.IsColourRows)
        {
            yield break;
        }

        foreach (var row in field.Definition.Kind == ActionParameterKind.Condition
                     ? []
                     : field.Definition.Semantics.Columns.Count > 0 ? StoredRows(field) : [])
        {
            yield return new EditorGenericRowSnapshot(row);
        }
    }

    private static IReadOnlyList<IReadOnlyList<string>> StoredRows(StepParameterViewModel field)
    {
        // The field's own row collection is not exposed for arbitrary kinds; the step snapshot already
        // carries the stored rows. This path is only for row kinds without a dedicated editor, and
        // those are rebuilt from the form's BuildStep by the caller when needed.
        _ = field;
        return [];
    }

    private static IReadOnlyList<EditorValuePartSnapshot> Parts(StepParameterViewModel field)
    {
        if (field.CurrentShape is ConcatNode run)
        {
            return [.. run.Parts.Select(part => part switch
            {
                TextNode text => new EditorValuePartSnapshot("text", text.Text, string.Empty),
                VariableNode name => new EditorValuePartSnapshot("name", string.Empty, name.Name),
                _ => new EditorValuePartSnapshot("text", Expression.Render(part), string.Empty),
            })];
        }

        return field.CurrentShape switch
        {
            TextNode text => [new EditorValuePartSnapshot("text", text.Text, string.Empty)],
            VariableNode name => [new EditorValuePartSnapshot("name", string.Empty, name.Name)],
            _ => [],
        };
    }

    private static EditorConditionSnapshot? Condition(StepParameterViewModel field)
    {
        if (field.Definition.Kind is not ActionParameterKind.Condition || field.ConditionEditor is not { } editor)
        {
            return null;
        }

        return new EditorConditionSnapshot(editor.Root.Join.ToString(), [.. editor.Root.Children.Select(Node)],
            true);
    }

    private static EditorConditionNodeSnapshot Node(ConditionNodeViewModel node)
        => node.IsGroup
            ? new EditorConditionNodeSnapshot("group", node.Join.ToString(), string.Empty, string.Empty,
                string.Empty, [.. node.Children.Select(Node)])
            : new EditorConditionNodeSnapshot("row", string.Empty, Expression.Render(node.LeftNode),
                node.Compare.ToString(), Expression.Render(node.RightNode), []);

    private static EditorSteppingMetaSnapshot Meta(AddActionViewModel form)
        => new(form.MetaEnabled, form.MetaComment, Duration(form.MetaTimeout),
            form.MetaRetryCount, Duration(form.MetaRetryDelay), Duration(form.MetaDelayBefore),
            Duration(form.MetaDelayAfter), form.MetaRetryBackoff?.Value ?? "fixed",
            form.MetaOnError?.Value ?? "stop", form.RetryPlan, form.TunedStep);

    private static EditorDurationSnapshot Duration(DurationSettingViewModel setting)
        => new(setting.Milliseconds,
            setting.Value is { } value
                ? value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty,
            setting.Unit.Key,
            [.. setting.Units.Select(unit => new EditorChoiceSnapshot(unit.Key, unit.Label))]);

    private static EditorDurationSnapshot Duration(int milliseconds, string? written,
        IReadOnlyList<EditorChoiceSnapshot> units)
        => new(milliseconds, written ?? string.Empty, string.Empty, units);

    private static string Number(decimal? value)
        => value is { } number
            ? number.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;

    private static IReadOnlyList<EditorChoiceSnapshot> Choices(IEnumerable<ActionParameterOption> choices)
        => [.. choices.Select(choice => new EditorChoiceSnapshot(choice.Value, choice.Display))];
}
