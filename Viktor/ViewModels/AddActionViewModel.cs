using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Viktor.Core.Execution;
using Viktor.Localization;
using Viktor.Models;

namespace Viktor.ViewModels;

/// <summary>
/// Drives the "Add Action" dialog: pick an action from the catalogue, fill in the
/// parameters it asks for, then save the result as one step.
/// </summary>
public partial class AddActionViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _variables;
    private readonly IReadOnlyList<string> _macros;

    /// <summary>Raised with the step to add, or <c>null</c> when the dialog is cancelled.</summary>
    public event Action<MacroStep?>? CloseRequested;

    /// <summary>Raised when a nested editor wants the picker opened to add a step.</summary>
    public event Action<StepListEditorViewModel>? NestedAddRequested;

    /// <summary>Raised when a nested editor wants the picker opened to edit a step.</summary>
    public event Action<MacroStep>? NestedEditRequested;

    public AddActionViewModel()
        : this(null, null, null)
    {
    }

    /// <summary>Creates the dialog, optionally restricted to a catalogue subset.</summary>
    public AddActionViewModel(IReadOnlyList<ActionDefinition>? actions,
        IReadOnlyList<string>? variables = null, IReadOnlyList<string>? macros = null)
    {
        AvailableActions = actions ?? ActionCatalog.Definitions;
        _variables = variables ?? [];
        _macros = macros ?? [];
        MetaOnError = ErrorChoices[0];
    }

    /// <summary>Everything the "Select Action" dropdown offers.</summary>
    public IReadOnlyList<ActionDefinition> AvailableActions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    [NotifyPropertyChangedFor(nameof(CommitLabel))]
    public partial bool IsEditing { get; set; }

    /// <summary>Title-bar text, which changes when an existing step is being edited.</summary>
    public string Header => Strings.Get(IsEditing ? "Add.EditTitle" : "Add.Title");

    /// <summary>Label of the confirm button.</summary>
    public string CommitLabel => Strings.Get(IsEditing ? "Add.CommitEdit" : "Add.Commit");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedKey))]
    [NotifyPropertyChangedFor(nameof(Description))]
    public partial ActionDefinition? SelectedDefinition { get; set; }

    /// <summary>Editors for the selected action, rebuilt whenever the selection changes.</summary>
    public ObservableCollection<StepParameterViewModel> Parameters { get; } = [];

    /// <summary>The parameters as the dialog shows them, with a position's x and y on one line.</summary>
    public ObservableCollection<ParameterRowViewModel> Rows { get; } = [];

    public bool HasSelection => SelectedDefinition is not null;

    public bool HasParameters => Parameters.Count > 0;

    /// <summary>Note shown under the step in the editor's list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial string MetaComment { get; set; } = string.Empty;

    /// <summary>False when the macro should skip this step.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial bool MetaEnabled { get; set; } = true;

    /// <summary>How long the step may run before it counts as failed. 0 means no limit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial decimal? MetaTimeoutMs { get; set; }

    /// <summary>How many extra attempts a failing step gets.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial decimal? MetaRetryCount { get; set; }

    /// <summary>Pause between two attempts.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial decimal? MetaRetryDelayMs { get; set; } = 500;

    /// <summary>Pause before the step runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial decimal? MetaDelayBeforeMs { get; set; }

    /// <summary>Pause after the step has run.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial decimal? MetaDelayAfterMs { get; set; }

    /// <summary>What the macro does when this step fails.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial ActionParameterOption MetaOnError { get; set; }

    /// <summary>Choices offered by the failure dropdown.</summary>
    public IReadOnlyList<ActionParameterOption> ErrorChoices { get; } =
    [
        new("stop", Strings.Get("Add.OnError.Stop")),
        new("continue", Strings.Get("Add.OnError.Continue")),
        new("nextIteration", Strings.Get("Add.OnError.NextIteration")),
        new("ask", Strings.Get("Add.OnError.AskUser")),
    ];

    /// <summary>Fully qualified name of the selected action, e.g. <c>control.delay</c>.</summary>
    public string SelectedKey => SelectedDefinition?.Key ?? Strings.Get("Add.NoSelection");

    public string Description => SelectedDefinition?.LocalDescription
        ?? Strings.Get("Add.SelectHint");

    /// <summary>
    /// Formatting for the preview only. The relaxed encoder keeps Chinese text and the
    /// operators an expression uses (<c>+ ( ) &gt; &amp;</c>) readable instead of writing
    /// them as <c>\uXXXX</c> escapes. The saved file keeps the default encoder.
    /// </summary>
    private static readonly JsonSerializerOptions PreviewFormat = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Live preview of the JSON node that will be appended to the macro tree.</summary>
    public string JsonPreview => SelectedDefinition is null
        ? Strings.Get("Add.JsonPlaceholder")
        : BuildStep().ToJson().ToJsonString(PreviewFormat);

    /// <summary>Explains why the step cannot be saved yet, or is empty when it can.</summary>
    public string ValidationMessage
    {
        get
        {
            if (SelectedDefinition is null)
            {
                return Strings.Get("Add.SelectFirst");
            }

            if (ScopeError() is { } error)
            {
                return error;
            }

            return MissingParameters() is { Count: > 0 } missing
                ? Strings.Format("Add.StillNeeded", string.Join(", ", missing))
                : string.Empty;
        }
    }

    public bool CanSave => SelectedDefinition is not null
        && ScopeError() is null
        && MissingParameters().Count == 0;

    partial void OnSelectedDefinitionChanged(ActionDefinition? value) => BuildParameters(value);

    /// <summary>
    /// Chooses an action by its catalogue key. A palette shortcut opens this dialog on the
    /// action it stands for, so the user only has to fill the blanks in.
    /// </summary>
    public void SelectAction(string key)
        => SelectedDefinition = AvailableActions.FirstOrDefault(definition => definition.Key == key);

    /// <summary>
    /// Variable names the pickers offer: the ones the editor already knows about plus
    /// any created by nested steps added in this dialog.
    /// </summary>
    public IReadOnlyList<string> CollectVariables()
    {
        var names = new SortedSet<string>(_variables, StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in Parameters)
        {
            var nested = parameter.List?.Steps;
            if (nested is null)
            {
                continue;
            }

            foreach (var step in nested)
            {
                step.CollectVariables(names);
            }
        }

        return [.. names];
    }

    /// <summary>Fills the dialog from an existing step so it can be edited.</summary>
    public void LoadFrom(MacroStep step)
    {
        IsEditing = true;
        SelectedDefinition = ActionCatalog.Find(step.Type);

        foreach (var editor in Parameters)
        {
            var stored = step.Parameters.FirstOrDefault(parameter => parameter.Name == editor.Definition.Name);
            if (stored is not null)
            {
                editor.ApplyValue(stored);
            }
        }

        MetaComment = step.Meta.Comment;
        MetaEnabled = step.Meta.IsEnabled;
        MetaTimeoutMs = step.Meta.TimeoutMs;
        MetaRetryCount = step.Meta.RetryCount;
        MetaRetryDelayMs = step.Meta.RetryDelayMs;
        MetaDelayBeforeMs = step.Meta.DelayBeforeMs;
        MetaDelayAfterMs = step.Meta.DelayAfterMs;
        MetaOnError = ErrorChoices.FirstOrDefault(choice =>
            choice.Value == StepMeta.Name(step.Meta.OnError)) ?? ErrorChoices[0];
    }

    private void BuildParameters(ActionDefinition? definition)
    {
        foreach (var parameter in Parameters)
        {
            parameter.PropertyChanged -= OnParameterChanged;

            if (parameter.List is not null)
            {
                parameter.List.PropertyChanged -= OnParameterChanged;
                parameter.List.AddRequested -= OnNestedAddRequested;
                parameter.List.EditRequested -= OnNestedEditRequested;
            }
        }

        Parameters.Clear();

        if (definition is not null)
        {
            var variables = CollectVariables();

            foreach (var parameter in definition.Parameters)
            {
                var editor = new StepParameterViewModel(parameter, variables, _macros);
                editor.PropertyChanged += OnParameterChanged;

                if (editor.List is not null)
                {
                    editor.List.PropertyChanged += OnParameterChanged;
                    editor.List.AddRequested += OnNestedAddRequested;
                    editor.List.EditRequested += OnNestedEditRequested;
                }

                Parameters.Add(editor);
            }

            MarkCoordinates();
            MarkRegion();
        }

        BuildRows();
        OnParameterChanged(this, new PropertyChangedEventArgs(nameof(Parameters)));
    }

    /// <summary>
    /// Lays the parameters out a line at a time, keeping the two halves of a screen position
    /// together so x and y sit side by side.
    /// </summary>
    private void BuildRows()
    {
        Rows.Clear();

        for (var index = 0; index < Parameters.Count; index++)
        {
            var current = Parameters[index];
            var next = index + 1 < Parameters.Count ? Parameters[index + 1] : null;

            if (current.IsRowPair && next?.IsRowPair == true
                || current.IsCoordinate && next?.IsCoordinate == true)
            {
                Rows.Add(new ParameterRowViewModel(current, next));
                index++;
                continue;
            }

            Rows.Add(new ParameterRowViewModel(current));
        }
    }

    /// <summary>
    /// Notes the parameters that make up a screen position, so either of them can take the
    /// pointer's current place. Only actions carrying both halves get the shortcut.
    /// </summary>
    private void MarkCoordinates()
    {
        var x = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "x");
        var y = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "y");
        if (x is null || y is null)
        {
            return;
        }

        x.IsCoordinate = true;
        y.IsCoordinate = true;
    }

    /// <summary>
    /// Notes the parameters that name a rectangle, so one button can take all of them from a drag
    /// on the screen: x, y, width and height; the two corners of a drag; or a single
    /// "x,y,width,height" field.
    /// </summary>
    private void MarkRegion()
    {
        var anchor =
            Pair("x", "y") is { } corner &&
            Parameters.Any(parameter => parameter.Definition.Name == "width") &&
            Parameters.Any(parameter => parameter.Definition.Name == "height")
                ? corner
                : Pair("startX", "startY") is { } drag &&
                  Parameters.Any(parameter => parameter.Definition.Name == "endX") &&
                  Parameters.Any(parameter => parameter.Definition.Name == "endY")
                    ? drag
                    : Parameter("region");

        if (anchor is not null)
        {
            anchor.IsRegionAnchor = true;
        }

        // The two corners of a drag read better side by side, the way x and y already do.
        PairRow("startX", "startY");
        PairRow("endX", "endY");

        StepParameterViewModel? Pair(string first, string second)
            => Parameter(first) is not null && Parameter(second) is not null ? Parameter(first) : null;

        void PairRow(string first, string second)
        {
            if (Parameter(first) is { } left && Parameter(second) is { } right)
            {
                left.IsRowPair = true;
                right.IsRowPair = true;
            }
        }
    }

    private StepParameterViewModel? Parameter(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Definition.Name == name);

    /// <summary>True when this action works on a rectangle the region picker can fill in.</summary>
    public bool HasRegion => Parameters.Any(parameter => parameter.IsRegionAnchor);

    /// <summary>
    /// Writes a screen position into the action's x and y, which is what Alt + X does while
    /// the dialog is open. Returns false when the action has no position to fill.
    /// </summary>
    public bool ApplyCursorPosition(int x, int y)
    {
        var filled = false;
        foreach (var parameter in Parameters)
        {
            if (!parameter.IsCoordinate)
            {
                continue;
            }

            parameter.SetNumber(parameter.Definition.Name == "x" ? x : y);
            filled = true;
        }

        return filled;
    }

    /// <summary>
    /// Lets a parameter react to the sibling list it waits on, such as the logic of a
    /// condition group, which only applies once there are two or more conditions.
    /// </summary>
    /// <summary>
    /// Writes a rectangle that was dragged on the screen into whichever shape this action uses:
    /// x/y/width/height, the two corners of a drag, or one "x,y,width,height" field coming from
    /// the region picker. Returns false when the action has nowhere to put it.
    /// </summary>
    public bool ApplyRegion(int x, int y, int width, int height)
    {
        var filled = false;

        foreach (var (name, value) in new (string Name, int Value)[]
                 {
                     ("x", x), ("y", y), ("width", width), ("height", height),
                     ("startX", x), ("startY", y), ("endX", x + width), ("endY", y + height),
                 })
        {
            if (Parameter(name) is { IsNumber: true } parameter)
            {
                parameter.SetNumber(value);
                filled = true;
            }
        }

        if (!filled && Parameter("region") is { } region)
        {
            region.Text = $"{x},{y},{width},{height}";
            filled = true;
        }

        return filled;
    }

    private void RefreshGates()
    {
        foreach (var editor in Parameters)
        {
            var siblingName = editor.Definition.EnabledBySibling;
            if (siblingName.Length == 0)
            {
                continue;
            }

            var sibling = Parameters.FirstOrDefault(parameter =>
                parameter.Definition.Name == siblingName);
            editor.UpdateGate(sibling?.List?.Steps.Count ?? 0);
        }
    }

    private void OnNestedAddRequested(StepListEditorViewModel list) => NestedAddRequested?.Invoke(list);

    private void OnNestedEditRequested(MacroStep step) => NestedEditRequested?.Invoke(step);

    private void OnParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshGates();
        OnPropertyChanged(nameof(HasParameters));
        OnPropertyChanged(nameof(JsonPreview));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

    private List<string> MissingParameters()
        => Parameters.Where(parameter => parameter.IsMissing)
            .Select(parameter => parameter.Definition.LocalLabel)
            .ToList();

    /// <summary>
    /// Rules that span several parameters. A macro may only change global variables the
    /// Variable Center already created, and the variables Viktor provides stay read-only.
    /// </summary>
    private string? ScopeError()
    {
        if (SelectedDefinition?.Key is not "control.setVariable")
        {
            return null;
        }

        var name = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "name")
            ?.CurrentText.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            return null;
        }

        if (VariableCatalog.IsSystem(name))
        {
            return Strings.Format("Add.SystemReadOnly", name);
        }

        var scope = Parameters.FirstOrDefault(parameter => parameter.Definition.Name == "scope")
            ?.CurrentText;

        return scope == "global" && !VariableCatalog.IsGlobal(name)
            ? Strings.Format("Add.GlobalMustExist", name)
            : null;
    }

    private MacroStep BuildStep() => new()
    {
        Type = SelectedDefinition!.Key,
        Parameters = Parameters
            .Where(parameter => parameter.IsIncluded)
            .Select(parameter => parameter.ToStepParameter())
            .ToList(),
        Meta = new StepMeta
        {
            Comment = MetaComment.Trim(),
            IsEnabled = MetaEnabled,
            TimeoutMs = Whole(MetaTimeoutMs),
            RetryCount = Whole(MetaRetryCount),
            RetryDelayMs = Whole(MetaRetryDelayMs),
            DelayBeforeMs = Whole(MetaDelayBeforeMs),
            DelayAfterMs = Whole(MetaDelayAfterMs),
            OnError = StepMeta.Action(MetaOnError.Value),
        },
    };

    /// <summary>Reads one of the millisecond boxes, treating an empty box as zero.</summary>
    private static int Whole(decimal? value) => value is null ? 0 : Math.Max(0, (int)value.Value);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => CloseRequested?.Invoke(BuildStep());

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
