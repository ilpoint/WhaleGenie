using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Viktor.Core.Devices;
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
    private string _assetFolder = string.Empty;

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
        // Steps, not the whole catalogue: a condition says what has to be true and only means
        // something inside an if, a while or a wait, so it is not something to add as a step.
        AvailableActions = actions ?? ActionCatalog.RunnableActions;
        _variables = variables ?? [];
        _macros = macros ?? [];
        MetaOnError = ErrorChoices[0];
        MetaRetryBackoff = BackoffChoices[0];
        MetaTimeout = new DurationSettingViewModel();
        MetaRetryDelay = new DurationSettingViewModel(500);
        MetaDelayBefore = new DurationSettingViewModel();
        MetaDelayAfter = new DurationSettingViewModel();

        // A length of time is held as a number plus the unit it reads in, so a change to either has
        // to refresh the preview the same way a change to an action parameter does.
        foreach (var setting in new[] { MetaTimeout, MetaRetryDelay, MetaDelayBefore, MetaDelayAfter })
        {
            setting.PropertyChanged += OnSettingChanged;
        }
    }

    /// <summary>Everything the "Select Action" dropdown offers.</summary>
    public IReadOnlyList<ActionDefinition> AvailableActions { get; }

    /// <summary>
    /// Where a picture taken from the screen is saved while this dialog is open, and where a
    /// relative picture value is looked up. The dialog that opened this one sets it, so a
    /// picture lands beside the macro package it belongs to.
    /// </summary>
    public string AssetFolder
    {
        get => _assetFolder;
        set
        {
            if (string.Equals(_assetFolder, value, StringComparison.Ordinal))
            {
                return;
            }

            _assetFolder = value;
            OnPropertyChanged();

            foreach (var parameter in Parameters)
            {
                parameter.AssetFolder = value;
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Header))]
    [NotifyPropertyChangedFor(nameof(CommitLabel))]
    public partial bool IsEditing { get; set; }

    /// <summary>
    /// The windows on the desktop, so a position picked off the screen can be stored the way a
    /// window-anchored step reads it back. The dialog fills this in; without it a picked position
    /// is written down as screen pixels, which is what every unanchored step holds anyway.
    /// </summary>
    public IWindowDevice? Windows { get; set; }

    /// <summary>
    /// The controls on the desktop, so a position picked off the screen can be stored the way a
    /// step anchored to a control reads it back. Filled in by the dialog beside
    /// <see cref="Windows"/>, and unused by a step that measures from anything else.
    /// </summary>
    public IUiDevice? Ui { get; set; }

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

    /// <summary>How long the step may run before it counts as failed. Empty means no limit.</summary>
    public DurationSettingViewModel MetaTimeout { get; }

    /// <summary>How many extra attempts a failing step gets.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    [NotifyPropertyChangedFor(nameof(RetryPlan))]
    public partial decimal? MetaRetryCount { get; set; }

    /// <summary>Pause between two attempts.</summary>
    public DurationSettingViewModel MetaRetryDelay { get; }

    /// <summary>Pause before the step runs.</summary>
    public DurationSettingViewModel MetaDelayBefore { get; }

    /// <summary>Pause after the step has run.</summary>
    public DurationSettingViewModel MetaDelayAfter { get; }

    /// <summary>What the macro does when this step fails.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial ActionParameterOption MetaOnError { get; set; }

    /// <summary>
    /// The step's error rules as they are typed, one to a line. They stay text until the step is
    /// saved, so a half-written line never has to mean anything in the meantime.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    public partial string MetaErrorJumps { get; set; } = string.Empty;

    /// <summary>Choices offered by the failure dropdown.</summary>
    public IReadOnlyList<ActionParameterOption> ErrorChoices { get; } =
    [
        new("stop", Strings.Get("Add.OnError.Stop")),
        new("continue", Strings.Get("Add.OnError.Continue")),
        new("nextIteration", Strings.Get("Add.OnError.NextIteration")),
        new("ask", Strings.Get("Add.OnError.AskUser")),
    ];

    /// <summary>How long to wait before each retry, read from the backoff dropdown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JsonPreview))]
    [NotifyPropertyChangedFor(nameof(RetryPlan))]
    public partial ActionParameterOption MetaRetryBackoff { get; set; }

    /// <summary>Choices offered by the backoff dropdown.</summary>
    public IReadOnlyList<ActionParameterOption> BackoffChoices { get; } =
    [
        new("fixed", Strings.Get("Add.Backoff.Fixed")),
        new("doubling", Strings.Get("Add.Backoff.Doubling")),
        new("jitter", Strings.Get("Add.Backoff.Jitter")),
    ];

    /// <summary>
    /// What the retry settings add up to, written out with the real numbers, so "double each
    /// attempt" is never a mystery about what the macro will actually do between two attempts.
    /// </summary>
    public string RetryPlan
    {
        get
        {
            var count = Whole(MetaRetryCount);
            if (count <= 0)
            {
                return Strings.Get("Add.StepRetryPlanNone");
            }

            var waits = new StepMeta
            {
                RetryCount = count,
                RetryDelayMs = MetaRetryDelay.Milliseconds,
                RetryBackoff = StepMeta.Backoff(MetaRetryBackoff?.Value ?? "fixed"),
            };

            return waits.RetryBackoff switch
            {
                RetryBackoff.Fixed => Strings.Format("Add.StepRetryPlanFixed", count,
                    FormatDuration(waits.RetryDelayFor(1))),
                RetryBackoff.Doubling => Strings.Format("Add.StepRetryPlanGrowing", count,
                    string.Join(" → ", RetryWaits(waits, count))),
                _ => Strings.Format("Add.StepRetryPlanRandom", count,
                    FormatDuration(waits.RetryDelayFor(1) / 2.0),
                    FormatDuration(waits.RetryDelayFor(1) * 1.5)),
            };
        }
    }

    /// <summary>The waits the first few retries would use, for the growing backoff.</summary>
    private static IEnumerable<string> RetryWaits(StepMeta settings, int count)
    {
        var shown = Math.Min(count, 6);
        for (var attempt = 1; attempt <= shown; attempt++)
        {
            yield return FormatDuration(settings.RetryDelayFor(attempt));
        }

        if (count > shown)
        {
            yield return "…";
        }
    }

    /// <summary>A length of time written with its unit, so "5 分" never turns back into 300000.</summary>
    private static string FormatDuration(double milliseconds) => DurationUnit.Written((decimal)milliseconds);

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

            if (!StepMeta.TryRead(MetaErrorJumps, out _, out var rule))
            {
                return Strings.Format("Add.BadErrorJump", rule);
            }

            return MissingParameters() is { Count: > 0 } missing
                ? Strings.Format("Add.StillNeeded", string.Join(", ", missing))
                : string.Empty;
        }
    }

    public bool CanSave => SelectedDefinition is not null
        && ScopeError() is null
        && StepMeta.TryRead(MetaErrorJumps, out _, out _)
        && MissingParameters().Count == 0;

    partial void OnSelectedDefinitionChanged(ActionDefinition? value) => BuildParameters(value);

    /// <summary>
    /// A rule that does not read is a reason not to save, so the two things that say so have to
    /// be told when the text changes — the same way changing a parameter does.
    /// </summary>
    partial void OnMetaErrorJumpsChanged(string value)
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(CanSave));
        SaveCommand.NotifyCanExecuteChanged();
    }

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
        MetaTimeout.Load(step.Meta.TimeoutMs);
        MetaRetryCount = step.Meta.RetryCount;
        MetaRetryDelay.Load(step.Meta.RetryDelayMs);
        MetaDelayBefore.Load(step.Meta.DelayBeforeMs);
        MetaDelayAfter.Load(step.Meta.DelayAfterMs);
        MetaOnError = ErrorChoices.FirstOrDefault(choice =>
            choice.Value == StepMeta.Name(step.Meta.OnError)) ?? ErrorChoices[0];
        MetaErrorJumps = StepMeta.Text(step.Meta.Jumps);
        MetaRetryBackoff = BackoffChoices.FirstOrDefault(choice =>
            choice.Value == StepMeta.Name(step.Meta.RetryBackoff)) ?? BackoffChoices[0];
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
                editor.AssetFolder = _assetFolder;
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
    /// Writes a position into the action's x and y, counted from the window the step is anchored
    /// to when it names one, which is what Alt + X does while the dialog is open. Returns false
    /// when the action has no position to fill.
    /// </summary>
    public bool ApplyCursorPosition(int x, int y)
    {
        var origin = AnchorOrigin();
        var filled = false;
        foreach (var parameter in Parameters)
        {
            if (!parameter.IsCoordinate)
            {
                continue;
            }

            parameter.SetNumber(parameter.Definition.Name == "x"
                ? x - (origin?.X ?? 0)
                : y - (origin?.Y ?? 0));
            filled = true;
        }

        return filled;
    }

    /// <summary>
    /// The corner the numbers of this action are counted from, so that a position read off the
    /// screen is stored the way the engine will read it back. Null when the action counts in
    /// screen pixels, and also when it names a window that is not open right now: there is
    /// nothing to subtract then, and the plain numbers are the best that can be done.
    /// </summary>
    private ScreenPoint? AnchorOrigin()
    {
        var mode = Parameter("anchorMode")?.CurrentText.Trim().ToLowerInvariant();
        if (mode == "element")
        {
            return ElementOrigin();
        }

        if (Windows is null || mode is not ("window" or "client"))
        {
            return null;
        }

        var title = Parameter("anchorWindow")?.Text?.Trim() ?? string.Empty;
        if (title.Length == 0 || Windows.Find(title, WindowMatch.Title) is not { } window)
        {
            return null;
        }

        return mode == "client" ? Windows.ClientOrigin(window.Handle) : window.Location;
    }

    /// <summary>
    /// Where the control a step is anchored to sits right now. Null when nothing has been picked
    /// yet, when the control is not on screen, or when the dialog has no way to look it up: the
    /// plain numbers are then the best that can be done, and the engine says so when the macro runs.
    /// </summary>
    private ScreenPoint? ElementOrigin()
    {
        var selector = Parameter("anchorSelector")?.Text?.Trim() ?? string.Empty;
        if (Ui is null || selector.Length == 0)
        {
            return null;
        }

        try
        {
            var query = UiQuery.Parse(selector, Parameter("anchorWindow")?.Text);
            return Ui.FindAll(query, 1).FirstOrDefault() is { } anchor ? anchor.Location : null;
        }
        catch (Exception)
        {
            // A control that cannot be looked up right now is not worth failing the dialog over:
            // the numbers are written down as screen pixels, and the step says what it needed when
            // the macro runs.
            return null;
        }
    }

    /// <summary>
    /// Writes a rectangle that was dragged on the screen into whichever shape this action uses:
    /// x/y/width/height, the two corners of a drag, or one "x,y,width,height" field coming from
    /// the region picker. A window-anchored action has the window's corner taken off first, the
    /// same way the position shortcut does. Returns false when the action has nowhere to put it.
    /// </summary>
    public bool ApplyRegion(int x, int y, int width, int height)
    {
        var origin = AnchorOrigin();
        var left = x - (origin?.X ?? 0);
        var top = y - (origin?.Y ?? 0);
        var filled = false;

        foreach (var (name, value) in new (string Name, int Value)[]
                 {
                     ("x", left), ("y", top), ("width", width), ("height", height),
                     ("startX", left), ("startY", top),
                     ("endX", left + width), ("endY", top + height),
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
            region.Text = $"{left},{top},{width},{height}";
            filled = true;
        }

        return filled;
    }

    /// <summary>
    /// Lets a parameter react to the sibling list it waits on, such as the logic of a
    /// condition group, which only applies once there are two or more conditions.
    /// </summary>
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

    /// <summary>Refreshes what a change to a step-settings length of time affects.</summary>
    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(JsonPreview));
        OnPropertyChanged(nameof(RetryPlan));
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
            TimeoutMs = MetaTimeout.Milliseconds,
            RetryCount = Whole(MetaRetryCount),
            RetryDelayMs = MetaRetryDelay.Milliseconds,
            RetryBackoff = StepMeta.Backoff(MetaRetryBackoff?.Value ?? "fixed"),
            DelayBeforeMs = MetaDelayBefore.Milliseconds,
            DelayAfterMs = MetaDelayAfter.Milliseconds,
            OnError = StepMeta.Action(MetaOnError.Value),
            Jumps = StepMeta.TryRead(MetaErrorJumps, out var jumps, out _) ? jumps : [],
        },
    };

    /// <summary>Reads one of the millisecond boxes, treating an empty box as zero.</summary>
    private static int Whole(decimal? value) => value is null ? 0 : Math.Max(0, (int)value.Value);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => CloseRequested?.Invoke(BuildStep());

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
