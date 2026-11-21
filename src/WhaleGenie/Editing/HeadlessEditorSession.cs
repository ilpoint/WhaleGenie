using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Editing;

/// <summary>
/// The editor as a session rather than a window: what C1's page will talk to. It owns the same
/// <see cref="MacroEditorViewModel"/> and the same <see cref="ProjectMacroCommit"/> rule the native
/// window used, so opening a macro, changing a field, adding a step and saving go through one C#
/// model. Nothing here needs Avalonia or a native window.
/// </summary>
internal sealed class HeadlessEditorSession
{
    private readonly ProjectWorkspace _workspace;
    private readonly MacroEditorViewModel _model;
    private readonly Dictionary<StepParameter, ConditionDraft> _conditionDrafts = [];
    private MacroItem? _existing;

    private HeadlessEditorSession(ProjectWorkspace workspace, MacroItem? existing)
    {
        _workspace = workspace;
        _existing = existing;
        _model = new MacroEditorViewModel
        {
            Variables = workspace.Variables,
            Persisted = workspace.Persisted,
        };
        _model.LoadProjectTriggerSettings(workspace.Triggers, [.. workspace.Macros]);
        if (existing is not null)
        {
            _model.LoadFrom(existing, workspace.Triggers.ForMacro(existing.Name));
        }
        else
        {
            _model.UseDefaultName(workspace.Macros.Select(macro => macro.Name));
        }
    }

    public static HeadlessEditorSession Open(ProjectWorkspace workspace, string? name)
    {
        if (name is not { Length: > 0 })
        {
            return new HeadlessEditorSession(workspace, null);
        }

        var existing = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidOperationException("the project has no macro named " + name);
        }

        return new HeadlessEditorSession(workspace, existing);
    }

    public string Name => _model.Name;

    public string Description => _model.Description;

    public string Group => _model.Group;

    public bool IsDirty => _model.IsDirty;

    public bool CanSave => _model.CanSave;

    public bool CanUndo => _model.CanUndo;

    public bool CanRedo => _model.CanRedo;

    public string SaveProblem => _model.NameProblem is { Length: > 0 } nameProblem
        ? nameProblem
        : _model.DeclarationProblemSummary is { Length: > 0 } declarationProblem
            ? declarationProblem
            : string.Empty;

    public IReadOnlyList<string> StepTypes
        => [.. _model.Draft().Macro.Steps.Select(step => step.Type)];

    public IReadOnlyList<EditorTriggerSnapshot> Triggers
        => [.. _model.TriggerEntries.Select(entry => new EditorTriggerSnapshot(
            entry.Id,
            entry.Kind.ToString(),
            entry.KindLabel,
            entry.Summary,
            entry.IsEnabled,
            entry.Spec is KeySpec key ? key.Binding : string.Empty,
            entry.Spec is KeySpec pressed ? pressed.Loop.ToString() : string.Empty,
            entry.Spec is KeySpec once && once.Once,
            entry.Spec is TimerSpec timer ? timer.Mode.ToString() : string.Empty,
            entry.Spec is TimerSpec timed ? timed.Time : string.Empty,
            entry.Spec is TimerSpec interval
                ? interval.Interval.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is TimerSpec unit ? unit.Unit.ToString() : string.Empty,
            entry.Spec is FileSpec file ? file.Path : string.Empty,
            entry.Spec is FileSpec change ? change.Change.ToString() : string.Empty,
            entry.Spec is FileSpec filter ? filter.Filter : string.Empty,
            entry.Spec is FileSpec folders && folders.Subfolders,
            entry.Spec is ProcessSpec process ? process.Name : string.Empty,
            entry.Spec is ProcessSpec processChange ? processChange.Change.ToString() : string.Empty,
            entry.Spec is HotstringSpec hotstring ? hotstring.Abbreviation : string.Empty,
            entry.Spec is ColorSpec colour ? colour.Match.ToString() : string.Empty,
            entry.Spec is ColorSpec hex ? hex.Hex : string.Empty,
            entry.Spec is ColorSpec tolerance
                ? tolerance.Tolerance.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec point
                ? point.X.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec pointY
                ? pointY.Y.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec follow && follow.FollowCursor,
            entry.Spec is IdleSpec idle
                ? idle.Seconds.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is WindowSpec window ? window.Target.Title : string.Empty,
            entry.Spec is WindowSpec processWindow ? processWindow.Target.Process : string.Empty,
            entry.Spec is WindowSpec classWindow ? classWindow.Target.ClassName : string.Empty,
            entry.Spec is WindowSpec howWindow ? howWindow.Target.How.ToString() : string.Empty,
            entry.Spec is WindowSpec changeWindow ? changeWindow.Change.ToString() : string.Empty,
            entry.Spec is InterfaceSpec face ? face.Window.Title : string.Empty,
            entry.Spec is InterfaceSpec faceProcess ? faceProcess.Window.Process : string.Empty,
            entry.Spec is InterfaceSpec faceHow ? faceHow.Window.How.ToString() : string.Empty))];

    public IReadOnlyList<EditorVariableSnapshot> Variables
        => [
            .. _model.DeclaredLocals.Select(local => new EditorVariableSnapshot(
                local.Name,
                WhaleGenie.Models.VariableTypes.Name(local.Type),
                Formula.Word(local.Initial),
                local.Description,
                Strings.Get("LocalVariables.Declared"),
                true)),
            .. _model.CollectVariables()
                .Where(choice => choice.Origin == VariableOrigin.Local)
                .OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)
                .Select(choice => new EditorVariableSnapshot(
                    choice.Name,
                    WhaleGenie.Models.VariableTypes.Name(choice.Type ?? ValueKind.Text),
                    string.Empty,
                    choice.Description,
                    choice.Source,
                    false)),
        ];

    public static IReadOnlyList<EditorTriggerKindSnapshot> TriggerKindOptions
        => [.. TriggerKinds.Editable.Select(kind => new EditorTriggerKindSnapshot(
            kind.ToString(), TriggerKinds.Text(kind)))];

    public static IReadOnlyList<EditorVariableTypeSnapshot> VariableTypes
        => [.. WhaleGenie.Models.VariableTypes.Declarable.Select(kind => new EditorVariableTypeSnapshot(
            WhaleGenie.Models.VariableTypes.Name(kind),
            Strings.Get($"Variable.Type.{WhaleGenie.Models.VariableTypes.Name(kind)}",
                WhaleGenie.Models.VariableTypes.Name(kind))))];

    public static IReadOnlyList<EditorConditionCompareSnapshot> ConditionCompares
        => [.. Condition.CompareChoices.Select(choice => new EditorConditionCompareSnapshot(
            choice.Compare.ToString(), choice.Label))];

    public IReadOnlyList<EditorValueChoiceSnapshot> ValueChoices
        => [.. _model.CollectVariables().Select(choice => new EditorValueChoiceSnapshot(
            choice.Name,
            choice.Type is { } type ? WhaleGenie.Models.VariableTypes.Name(type) : "text",
            choice.Origin.ToString(),
            choice.Source,
            choice.Description))];

    public IReadOnlyList<EditorGlobalSnapshot> Globals
        => [.. _workspace.Variables.Globals.Select(variable => new EditorGlobalSnapshot(
            variable.Name,
            WhaleGenie.Models.VariableTypes.Name(variable.Type),
            Formula.Word(variable.Default),
            variable.Description,
            variable.PersistValue))];

    public void Patch(string? name = null, string? description = null, string? group = null)
    {
        if (name is not null)
        {
            _model.Name = name;
        }

        if (description is not null)
        {
            _model.Description = description;
        }

        if (group is not null)
        {
            _model.Group = group;
        }
    }

    public void Undo() => _model.UndoCommand.Execute(null);

    public void Redo() => _model.RedoCommand.Execute(null);

    public void AddStep(string type, string? parentId = null)
    {
        if (parentId is not { Length: > 0 })
        {
            _model.AddStep(NewStep(type));
            return;
        }

        var parent = FindStep(parentId)
            ?? throw new InvalidOperationException("the macro has no step " + parentId);
        var list = parent.Parameters.FirstOrDefault(parameter =>
            parameter.Kind == ActionParameterKind.Steps)?.Steps
            ?? throw new InvalidOperationException("step " + parentId + " holds no sub-steps");
        list.Add(NewStep(type));
        _model.IsDirty = true;
    }

    public void AddGlobal()
    {
        var name = "variable";
        for (var index = 2; _workspace.Variables.FindGlobal(name) is not null; index++)
        {
            name = "variable" + index.ToString(CultureInfo.InvariantCulture);
        }

        _workspace.Variables.Adopt(new VariableDefinition { Name = name });
        _workspace.MarkGlobalsChanged();
    }

    public void PatchGlobal(string originalName, string field, string value)
    {
        var global = _workspace.Variables.FindGlobal(originalName)
            ?? throw new InvalidOperationException("the project has no global " + originalName);
        var next = value.Trim();
        switch (field)
        {
            case "name":
                VariableRename.RenameGlobal([.. _workspace.Macros], originalName, next);
                _model.RenameGlobalReferences(originalName, next);
                _workspace.Persisted.Rename(originalName, next);
                _workspace.Variables.Values.Rename(originalName, next);
                global.Name = next;
                break;
            case "type":
                global.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "default":
                global.Default = Formula.Text(value);
                break;
            case "description":
                global.Description = value;
                break;
            case "persist":
                global.PersistValue = bool.TryParse(value, out var persist) && persist;
                break;
            default:
                throw new InvalidOperationException("unknown global field " + field);
        }

        _workspace.MarkGlobalsChanged();
    }

    public void RemoveGlobal(string name)
    {
        var global = _workspace.Variables.FindGlobal(name)
            ?? throw new InvalidOperationException("the project has no global " + name);
        var kept = _workspace.Variables.Globals
            .Where(candidate => !ReferenceEquals(candidate, global))
            .ToList();
        _workspace.Variables.ClearGlobals();
        foreach (var candidate in kept)
        {
            _workspace.Variables.Adopt(candidate);
        }

        _workspace.Persisted.Forget(name);
        _workspace.Variables.Values.Remove(name);
        _workspace.MarkGlobalsChanged();
    }

    public void AddVariable()
    {
        _model.DeclaredLocals.Add(new MacroLocalVariable { Name = string.Empty, Type = ValueKind.Text });
        _model.IsDirty = true;
    }

    public void PatchVariable(string originalName, string field, string value)
    {
        var local = _model.DeclaredLocals.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), originalName.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no local variable " + originalName);
        switch (field)
        {
            case "name":
                if (!_model.RenameLocalVariable(originalName, value, out var problem))
                {
                    throw new InvalidOperationException(problem);
                }

                break;
            case "type":
                local.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "initial":
                local.Initial = Formula.Text(value);
                break;
            case "description":
                local.Description = value;
                break;
            default:
                throw new InvalidOperationException("unknown variable field " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveVariable(string name)
    {
        var local = _model.DeclaredLocals.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no local variable " + name);
        _model.DeclaredLocals.Remove(local);
        _model.IsDirty = true;
    }

    public void AddTrigger(string kind)
    {
        if (!Enum.TryParse<TriggerKind>(kind, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException("unknown trigger kind " + kind);
        }

        _model.AddTriggerCommand.Execute(parsed);
        _model.IsDirty = true;
    }

    public void AddKeyTrigger() => AddTrigger(nameof(TriggerKind.Key));

    public void RemoveTrigger(string triggerId)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal));
        if (trigger is null)
        {
            throw new InvalidOperationException("the macro has no trigger " + triggerId);
        }

        _model.RemoveTriggerCommand.Execute(trigger);
    }

    public void SetTriggerBinding(string triggerId, string binding)
    {
        var trigger = KeyTrigger(triggerId);
        _model.BindKey = binding.Trim();
        _model.IsDirty = true;
    }

    public void SetTriggerLoop(string triggerId, string loop)
    {
        var trigger = KeyTrigger(triggerId);
        if (!Enum.TryParse<MacroLoop>(loop, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException("unknown trigger loop " + loop);
        }

        _model.LoopMode = parsed;
        _model.IsDirty = true;
    }

    public void SetTriggerInterface(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not InterfaceSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an interface trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.WindowTarget = field switch
        {
            "title" => _model.WindowTarget with { Title = value },
            "process" => _model.WindowTarget with { Process = value },
            "how" => _model.WindowTarget with { How = value },
            _ => throw new InvalidOperationException("unknown interface field " + field),
        };
        _model.IsDirty = true;
    }

    public void SetTriggerWindow(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not WindowSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a window trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "title":
                _model.WindowTarget = _model.WindowTarget with { Title = value };
                break;
            case "process":
                _model.WindowTarget = _model.WindowTarget with { Process = value };
                break;
            case "class":
                _model.WindowTarget = _model.WindowTarget with { ClassName = value };
                break;
            case "how":
                _model.WindowTarget = _model.WindowTarget with { How = value };
                break;
            case "change":
                _model.WindowChange = Enum.Parse<WindowChangeKind>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown window field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerColor(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not ColorSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a colour trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "match":
                _model.ColorMatch = Enum.Parse<ColorMatchCondition>(value, ignoreCase: true);
                break;
            case "hex":
                _model.HexColor = value.Trim();
                break;
            case "tolerance":
                _model.ColorTolerance = value.Trim();
                break;
            case "x":
                _model.ColorPositionX = value.Trim();
                break;
            case "y":
                _model.ColorPositionY = value.Trim();
                break;
            case "follow":
                _model.UseCursorPosition = bool.TryParse(value, out var follow) && follow;
                break;
            default:
                throw new InvalidOperationException("unknown colour field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerIdle(string triggerId, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not IdleSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an idle trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.IdleSeconds = int.TryParse(value, out var seconds) ? Math.Max(1, seconds) : 60;
        _model.IsDirty = true;
    }

    public void SetTriggerProcess(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not ProcessSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a process trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "name":
                _model.ProcessName = value.Trim();
                break;
            case "change":
                _model.ProcessChange = Enum.Parse<ProcessChangeKind>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown process field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerHotstring(string triggerId, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not HotstringSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a hotstring trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.HotstringText = value;
        _model.IsDirty = true;
    }

    public void SetTriggerFile(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not FileSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a file trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "path":
                _model.WatchPath = value;
                break;
            case "change":
                _model.WatchChange = Enum.Parse<FileChangeKind>(value, ignoreCase: true);
                break;
            case "filter":
                _model.WatchFilter = value;
                break;
            case "subfolders":
                _model.WatchSubfolders = bool.TryParse(value, out var subfolders) && subfolders;
                break;
            default:
                throw new InvalidOperationException("unknown file field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerTimer(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not TimerSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a timer trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "mode":
                _model.ScheduleMode = Enum.Parse<ScheduleMode>(value, ignoreCase: true);
                break;
            case "time":
                _model.ScheduleTime = value.Trim();
                break;
            case "interval":
                _model.ScheduleInterval = int.TryParse(value, out var interval) ? interval : 1;
                break;
            case "unit":
                _model.ScheduleUnit = Enum.Parse<ScheduleUnit>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown timer field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerEnabled(string triggerId, bool enabled)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        trigger.IsEnabled = enabled;
        _model.IsDirty = true;
    }

    public void SetTriggerOnce(string triggerId, bool once)
    {
        _ = KeyTrigger(triggerId);
        _model.TriggerOnce = once;
        _model.IsDirty = true;
    }

    private TriggerEntry KeyTrigger(string triggerId)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not KeySpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a key trigger");
        }

        // The drawer's fields are what BuildMacro commits; move the selection onto this entry and
        // write the value there rather than changing a spec the next commit would overwrite.
        _model.SelectedTrigger = trigger;
        return trigger;
    }

    public void PatchParameter(string stepId, string name, string value)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var parameter = step.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("step " + stepId + " has no parameter " + name);
        parameter.Written = WrittenFor(parameter.Kind, value);
        _model.IsDirty = true;
    }

    public void SetStepEnabled(string stepId, bool enabled)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        step.Meta = step.Meta.WithEnabled(enabled);
        _model.IsDirty = true;
    }

    public void SetStepMeta(string stepId, string field, string value)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var meta = step.Meta;
        step.Meta = field switch
        {
            "comment" => CopyMeta(meta, comment: value),
            "timeoutMs" => CopyMeta(meta, timeoutMs: Number(value, meta.TimeoutMs)),
            "retryCount" => CopyMeta(meta, retryCount: Number(value, meta.RetryCount)),
            "retryDelayMs" => CopyMeta(meta, retryDelayMs: Number(value, meta.RetryDelayMs)),
            "onError" => CopyMeta(meta, onError: Enum.Parse<StepErrorAction>(value, ignoreCase: true)),
            _ => throw new InvalidOperationException("unknown step meta field " + field),
        };
        _model.IsDirty = true;
    }

    private static int Number(string value, int fallback)
        => int.TryParse(value, out var parsed) ? Math.Max(0, parsed) : fallback;

    private static StepMeta CopyMeta(StepMeta meta, string? comment = null, int? timeoutMs = null,
        int? retryCount = null, int? retryDelayMs = null, StepErrorAction? onError = null)
        => new()
        {
            Comment = comment ?? meta.Comment,
            IsEnabled = meta.IsEnabled,
            TimeoutMs = timeoutMs ?? meta.TimeoutMs,
            RetryCount = retryCount ?? meta.RetryCount,
            RetryDelayMs = retryDelayMs ?? meta.RetryDelayMs,
            RetryBackoff = meta.RetryBackoff,
            DelayBeforeMs = meta.DelayBeforeMs,
            DelayAfterMs = meta.DelayAfterMs,
            OnError = onError ?? meta.OnError,
        };

    public void AddConditionRow(string stepId, string name)
        => ChangeCondition(stepId, name, rows => rows.Add(new ConditionShape.Row(
            Formula.Nothing, ConditionCompare.Equal, Formula.Nothing)));

    public void RemoveConditionRow(string stepId, string name, int index)
        => ChangeCondition(stepId, name, rows =>
        {
            if (index < 0 || index >= rows.Count)
            {
                throw new InvalidOperationException("the condition has no row " + index);
            }

            rows.RemoveAt(index);
        });

    public void SetConditionRow(string stepId, string name, int index, string field, string value)
        => ChangeCondition(stepId, name, rows =>
        {
            if (index < 0 || index >= rows.Count)
            {
                throw new InvalidOperationException("the condition has no row " + index);
            }

            var row = rows[index];
            rows[index] = field switch
            {
                "left" => row with { Left = ConditionValue(value) },
                "right" => row with { Right = ConditionValue(value) },
                "compare" => row with { Compare = Enum.Parse<ConditionCompare>(value, ignoreCase: true) },
                _ => throw new InvalidOperationException("unknown condition field " + field),
            };
        });

    public void SetConditionJoin(string stepId, string name, string value)
        => ChangeCondition(stepId, name, _ => { }, Enum.Parse<Join>(value, ignoreCase: true));

    private static ExpressionNode ConditionValue(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return Formula.Nothing;
        }

        if (text[0] == '$')
        {
            return Formula.Name(text[1..]);
        }

        if (bool.TryParse(text, out var flag))
        {
            return Formula.Constant(flag ? "true" : "false");
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Formula.Number(text, number)
            : Formula.Text(value);
    }

    private void ChangeCondition(string stepId, string name, Action<List<ConditionShape.Row>> change,
        Join? join = null)
    {
        var parameter = FindParameter(stepId, name);
        if (!_conditionDrafts.TryGetValue(parameter, out var draft))
        {
            if (!Condition.TryRead(parameter.Written, out var shape))
            {
                throw new InvalidOperationException("the condition cannot be edited as rows");
            }

            var group = shape as ConditionShape.Group;
            draft = new ConditionDraft(
                group?.Join ?? Join.All,
                (group?.Children ?? [shape])
                .Select(child => child as ConditionShape.Row
                    ?? throw new InvalidOperationException("the condition has nested groups"))
                .ToList());
        }

        change(draft.Rows);
        draft = draft with { Join = join ?? draft.Join };
        _conditionDrafts[parameter] = draft;
        parameter.Written = Condition.ToNode(new ConditionShape.Group(draft.Join, draft.Rows))
            ?? Formula.Nothing;
        _model.IsDirty = true;
    }

    private sealed record ConditionDraft(Join Join, List<ConditionShape.Row> Rows);

    public void AddParameterRow(string stepId, string name)
    {
        var parameter = FindParameter(stepId, name);
        parameter.Rows.Add(new StepParameterRow());
        _model.IsDirty = true;
    }

    public void RemoveParameterRow(string stepId, string name, int index)
    {
        var parameter = FindParameter(stepId, name);
        if (index < 0 || index >= parameter.Rows.Count)
        {
            throw new InvalidOperationException("step " + stepId + " has no row " + index);
        }

        parameter.Rows.RemoveAt(index);
        _model.IsDirty = true;
    }

    public void PatchParameterRow(string stepId, string name, int index, string column, string value)
    {
        var parameter = FindParameter(stepId, name);
        if (index < 0 || index >= parameter.Rows.Count)
        {
            throw new InvalidOperationException("step " + stepId + " has no row " + index);
        }

        parameter.Rows[index].SetLine(column, value);
        _model.IsDirty = true;
    }

    private StepParameter FindParameter(string stepId, string name)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        return step.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("step " + stepId + " has no parameter " + name);
    }

    public void MoveStep(string stepId, int delta)
    {
        var found = Find(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var target = Math.Clamp(found.Index + delta, 0, found.List.Count - 1);
        if (target == found.Index)
        {
            return;
        }

        var step = found.List[found.Index];
        found.List.RemoveAt(found.Index);
        found.List.Insert(target, step);
        _model.IsDirty = true;
    }

    public void AppendSteps(IReadOnlyList<MacroStep> steps)
    {
        _model.AppendSteps(steps);
        _model.SettleStepIds();
        _model.IsDirty = true;
    }

    public void DuplicateStep(string stepId)
    {
        var found = Find(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var copy = MacroStep.FromJson(found.List[found.Index].ToJson());
        found.List.Insert(found.Index + 1, copy);
        _model.SettleStepIds();
        _model.IsDirty = true;
    }

    public void RemoveStep(string stepId)
    {
        var found = Find(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        found.List.RemoveAt(found.Index);
        _model.IsDirty = true;
    }

    /// <summary>Writes the draft through the same rule the native editor and the MCP save use.</summary>
    public bool Save()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException(SaveProblem is { Length: > 0 } problem
                ? problem
                : Strings.Get("Editor.Problem.CannotSave"));
        }

        var commit = new ProjectMacroCommit(_workspace, _existing, MacroView.All,
            _ => { }, () => { }, () => { });
        var draft = _model.Draft();
        commit.Apply(draft, []);
        _model.IsDirty = false;
        _existing = _workspace.Macros.FirstOrDefault(candidate => candidate.Name == draft.Macro.Name);
        return _existing is not null;
    }

    /// <summary>What the page will draw: the macro's fields and its step tree as the engine reads it.</summary>
    public EditorSnapshot Snapshot()
    {
        _model.SettleStepIds();
        var macro = _model.Draft().Macro;
        return new EditorSnapshot(
            Name,
            Description,
            Group,
            IsDirty,
            Count(macro.Steps),
            [.. macro.Steps.Select(StepSnapshot)],
            Triggers,
            Variables,
            Globals,
            ValueChoices,
            ConditionCompares,
            VariableTypes,
            TriggerKindOptions,
            CanUndo,
            CanRedo);
    }

    /// <summary>The action catalogue, in the order the picker shows it and with its form fields.</summary>
    public static IReadOnlyList<EditorActionSnapshot> Actions()
        => [.. ActionCatalog.Definitions.Select(action => new EditorActionSnapshot(
            action.Key,
            action.LocalName,
            action.Category.ToString(),
            Strings.Get($"Add.Category.{action.Category}", action.Category.ToString()),
            action.LocalDescription,
            action.Hidden,
            [.. action.Parameters
                .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
                .Select(parameter => new EditorActionParameterSnapshot(
                    parameter.Name,
                    parameter.LocalLabel,
                    parameter.Kind.ToString(),
                    parameter.Required,
                    parameter.Placeholder,
                    [.. parameter.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display))]))]))];

    private static int Count(IReadOnlyList<MacroStep> steps)
        => steps.Count + steps.Sum(step => step.Parameters
            .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
            .Sum(parameter => Count(parameter.Steps)));

    private EditorConditionSnapshot ConditionSnapshot(StepParameter parameter)
    {
        if (_conditionDrafts.TryGetValue(parameter, out var draft))
        {
            return new EditorConditionSnapshot(
                draft.Join.ToString(),
                [.. draft.Rows.Select(row => new EditorConditionRowSnapshot(
                    Expression.Render(row.Left),
                    row.Compare.ToString(),
                    Expression.Render(row.Right)))],
                true);
        }

        if (!Condition.TryRead(parameter.Written, out var shape))
        {
            return new EditorConditionSnapshot("All", [], false);
        }

        var group = shape as ConditionShape.Group;
        var children = group?.Children ?? [shape];
        if (children.Any(child => child is not ConditionShape.Row))
        {
            return new EditorConditionSnapshot("All", [], false);
        }

        return new EditorConditionSnapshot(
            (group?.Join ?? Join.All).ToString(),
            [.. children.Cast<ConditionShape.Row>().Select(row => new EditorConditionRowSnapshot(
                Expression.Render(row.Left),
                row.Compare.ToString(),
                Expression.Render(row.Right)))],
            true);
    }

    private static MacroStep NewStep(string type)
    {
        var definition = ActionCatalog.Definitions.FirstOrDefault(action => action.Key == type)
            ?? throw new InvalidOperationException("unknown action " + type);
        var step = new MacroStep { Type = type };
        foreach (var field in definition.Parameters)
        {
            step.Parameters.Add(new StepParameter
            {
                Name = field.Name,
                Kind = field.Kind,
                Written = field.Kind == ActionParameterKind.Steps
                    ? Formula.Nothing
                    : WrittenFor(field.Kind, field.DefaultValue),
            });
        }

        return step;
    }

    private static ExpressionNode WrittenFor(ActionParameterKind kind, string value)
        => kind switch
        {
            ActionParameterKind.Bool or ActionParameterKind.Choice => Formula.Constant(value),
            ActionParameterKind.Number
                when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number) => Formula.Number(value, number),
            _ => Formula.Text(value),
        };

    private EditorStepSnapshot StepSnapshot(MacroStep step)
    {
        var definition = ActionCatalog.Definitions.FirstOrDefault(action => action.Key == step.Type);
        var parameters = step.Parameters
            .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
            .Select(parameter =>
            {
                var field = definition?.Parameters.FirstOrDefault(candidate =>
                    candidate.Name == parameter.Name);
                var columns = field?.Semantics.Columns ?? [];
                return new EditorParameterSnapshot(
                    parameter.Name,
                    field?.LocalLabel ?? parameter.Name,
                    parameter.Kind.ToString(),
                    parameter.Value,
                    [.. field?.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display)) ?? []],
                    [.. columns.Select(column => new EditorColumnSnapshot(column.Name, column.Required))],
                    [.. parameter.Rows.Select(row => new EditorRowSnapshot(
                        [.. columns.Select(column => row.Text(column.Name))]))],
                    parameter.Kind == ActionParameterKind.Condition
                        ? ConditionSnapshot(parameter)
                        : null);
            })
            .ToList();
        var children = step.Parameters
            .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
            .SelectMany(parameter => parameter.Steps)
            .Select(StepSnapshot)
            .ToList();
        var acceptsSteps = definition?.Parameters.Any(parameter =>
            parameter.Kind == ActionParameterKind.Steps) == true;
        return new EditorStepSnapshot(step.Id, step.Type,
            definition?.LocalName ?? step.Type, step.Meta.IsEnabled, step.Meta.Comment,
            step.Meta.TimeoutMs, step.Meta.RetryCount, step.Meta.RetryDelayMs,
            step.Meta.OnError.ToString(), acceptsSteps, parameters, children);
    }

    private MacroStep? FindStep(string stepId)
        => Find(stepId) is { } found ? found.List[found.Index] : null;

    private (IList<MacroStep> List, int Index)? Find(string stepId)
    {
        foreach (var (list, index) in Walk(_model.Steps))
        {
            if (string.Equals(list[index].Id, stepId, StringComparison.Ordinal))
            {
                return (list, index);
            }
        }

        return null;
    }

    private static IEnumerable<(IList<MacroStep> List, int Index)> Walk(IList<MacroStep> steps)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            yield return (steps, index);
            foreach (var children in steps[index].Parameters
                         .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
                         .SelectMany(parameter => Walk(parameter.Steps)))
            {
                yield return children;
            }
        }
    }
}

internal sealed record EditorSnapshot(string Name, string Description, string Group, bool Dirty, int StepCount,
    IReadOnlyList<EditorStepSnapshot> Steps, IReadOnlyList<EditorTriggerSnapshot> Triggers,
    IReadOnlyList<EditorVariableSnapshot> Variables, IReadOnlyList<EditorGlobalSnapshot> Globals,
    IReadOnlyList<EditorValueChoiceSnapshot> ValueChoices,
    IReadOnlyList<EditorConditionCompareSnapshot> ConditionCompares,
    IReadOnlyList<EditorVariableTypeSnapshot> VariableTypes,
    IReadOnlyList<EditorTriggerKindSnapshot> TriggerKinds, bool CanUndo, bool CanRedo);

internal sealed record EditorGlobalSnapshot(string Name, string Type, string Default, string Description,
    bool Persisted);

internal sealed record EditorValueChoiceSnapshot(string Name, string Type, string Origin, string Source,
    string Description);

internal sealed record EditorTriggerKindSnapshot(string Value, string Label);

internal sealed record EditorVariableSnapshot(string Name, string Type, string Initial, string Description,
    string Source, bool IsDeclared);

internal sealed record EditorVariableTypeSnapshot(string Value, string Label);

internal sealed record EditorTriggerSnapshot(string Id, string Kind, string KindLabel, string Summary,
    bool Enabled, string Binding, string Loop, bool Once, string TimerMode, string Time,
    string Interval, string Unit, string FilePath, string FileChange, string FileFilter,
    bool FileSubfolders, string ProcessName, string ProcessChange, string HotstringText,
    string ColorMatch, string ColorHex, string ColorTolerance, string ColorX, string ColorY,
    bool FollowCursor, string IdleSeconds, string WindowTitle, string WindowProcess,
    string WindowClass, string WindowHow, string WindowChange, string InterfaceTitle,
    string InterfaceProcess, string InterfaceHow);

internal sealed record EditorStepSnapshot(string Id, string Type, string Name, bool Enabled,
    string Comment, int TimeoutMs, int RetryCount, int RetryDelayMs, string OnError,
    bool AcceptsSteps, IReadOnlyList<EditorParameterSnapshot> Parameters,
    IReadOnlyList<EditorStepSnapshot> Steps);

internal sealed record EditorParameterSnapshot(string Name, string Label, string Kind, string Value,
    IReadOnlyList<EditorChoiceSnapshot> Options, IReadOnlyList<EditorColumnSnapshot> Columns,
    IReadOnlyList<EditorRowSnapshot> Rows, EditorConditionSnapshot? Condition);

internal sealed record EditorConditionSnapshot(string Join, IReadOnlyList<EditorConditionRowSnapshot> Rows,
    bool Editable);

internal sealed record EditorConditionRowSnapshot(string Left, string Compare, string Right);

internal sealed record EditorConditionCompareSnapshot(string Value, string Label);

internal sealed record EditorColumnSnapshot(string Name, bool Required);

internal sealed record EditorRowSnapshot(IReadOnlyList<string> Cells);

internal sealed record EditorChoiceSnapshot(string Value, string Label);

internal sealed record EditorActionSnapshot(string Key, string Name, string Category,
    string CategoryLabel, string Description, bool Hidden,
    IReadOnlyList<EditorActionParameterSnapshot> Parameters);

internal sealed record EditorActionParameterSnapshot(string Name, string Label, string Kind, bool Required,
    string Placeholder, IReadOnlyList<EditorChoiceSnapshot> Options);
