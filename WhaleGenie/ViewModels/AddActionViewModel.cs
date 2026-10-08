using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

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
        // A step that fails is usually a surprise the macro cannot have planned for, so a new one
        // starts by stopping and asking rather than bringing the whole run down on its own.
        MetaOnError = ErrorChoices.First(choice => choice.Value == StepMeta.Name(StepErrorAction.AskUser));
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

        RebuildActions();
    }

    /// <summary>Everything the "Select Action" dropdown offers.</summary>
    public IReadOnlyList<ActionDefinition> AvailableActions { get; }

    /// <summary>What the picker's search box holds. Blank shows the whole catalogue.</summary>
    [ObservableProperty]
    public partial string ActionSearch { get; set; } = string.Empty;

    /// <summary>
    /// The catalogue as the picker shows it: recently used first, then the blocks, then a group a
    /// category.
    /// </summary>
    public ObservableCollection<ActionGroupViewModel> ActionGroups { get; } = [];

    /// <summary>
    /// True while the picker itself is on screen. Choosing an action folds it down to one line, so
    /// the fields it is about to fill are not pushed off the bottom of the dialog.
    /// </summary>
    [ObservableProperty]
    public partial bool IsPickerOpen { get; set; } = true;

    /// <summary>True when the search box has hidden everything, so the picker can say so.</summary>
    public bool HasNoActionMatch => ActionGroups.Count == 0;

    /// <summary>
    /// The actions used most recently, newest first. Shared by every dialog in the run, so the
    /// second step of a macro can start from what the first one used.
    /// </summary>
    private static readonly List<string> RecentlyUsed = [];

    /// <summary>How many actions the "recently used" group holds at most.</summary>
    private const int RecentLimit = 8;

    /// <summary>Key of the group that holds those, which is not a category.</summary>
    private const string RecentGroup = "recent";

    /// <summary>Key of the group that holds the blocks, which is not a category either.</summary>
    private const string BlockGroup = "blocks";

    /// <summary>
    /// The order the blocks are listed in: the run in order, the four repeats, then the branch,
    /// the many-way branch and the tidy-up. This is the order a task is built in, and it puts the
    /// four things that all "repeat" next to each other where they can be told apart.
    /// </summary>
    private static readonly string[] BlockOrder =
    [
        "control.sequence",
        "control.repeat",
        "control.while",
        "control.for",
        "control.forEach",
        "control.if",
        "control.switch",
        "control.try",
    ];

    /// <summary>Groups the user opened by hand, so a search does not fold them back up.</summary>
    private readonly HashSet<string> _openedGroups = new(StringComparer.Ordinal);

    /// <summary>The action being built, on the line the folded-away picker leaves behind.</summary>
    public string SelectedActionTitle => SelectedDefinition is null
        ? Strings.Get("Add.NoSelection")
        : SelectedDefinition.Key + " · " + SelectedDefinition.LocalName;

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
    [NotifyPropertyChangedFor(nameof(SelectedActionTitle))]
    [NotifyPropertyChangedFor(nameof(IsBlock))]
    public partial ActionDefinition? SelectedDefinition { get; set; }

    /// <summary>
    /// True when the step being edited holds steps of its own. The settings every step carries
    /// then read as being about the whole block: a failure inside it that no step handled is this
    /// block failing, and the block is what gets retried or left out.
    /// </summary>
    public bool IsBlock => SelectedDefinition is { } definition && HoldsSteps(definition);

    /// <summary>Editors for the selected action, rebuilt whenever the selection changes.</summary>
    public ObservableCollection<StepParameterViewModel> Parameters { get; } = [];

    /// <summary>The parameters as the dialog shows them, with a position's x and y on one line.</summary>
    public ObservableCollection<ParameterRowViewModel> Rows { get; } = [];

    /// <summary>The settings the dialog keeps folded away until they are asked for.</summary>
    public ObservableCollection<ParameterRowViewModel> AdvancedRows { get; } = [];

    /// <summary>True when this action has settings behind the fold.</summary>
    public bool HasAdvanced => AdvancedRows.Count > 0;

    /// <summary>
    /// True while the folded settings are on screen. A step that already uses one opens with them
    /// showing, so the reason it behaves unusually is never hidden from the person editing it.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AdvancedLabel))]
    [NotifyPropertyChangedFor(nameof(AdvancedCaret))]
    public partial bool ShowAdvanced { get; set; }

    /// <summary>Text of the fold's button, which says how many settings are behind it.</summary>
    public string AdvancedLabel => Strings.Format(
        ShowAdvanced ? "Add.AdvancedHide" : "Add.AdvancedShow", AdvancedRows.Count);

    /// <summary>The mark on that button, pointing the way the fold will go.</summary>
    public Geometry AdvancedCaret => ShowAdvanced ? Carets.Open : Carets.Shut;

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

            return MissingParameters() is { Count: > 0 } missing
                ? Strings.Format("Add.StillNeeded", string.Join(", ", missing))
                : string.Empty;
        }
    }

    /// <summary>
    /// True when <see cref="ValidationMessage"/> is a problem to fix rather than the empty dialog's
    /// "pick an action first". Only a problem is worth shouting about: a line written in red as
    /// soon as the dialog opens would report a mistake nobody has had the chance to make.
    /// </summary>
    public bool ValidationIsProblem => SelectedDefinition is not null && ValidationMessage.Length > 0;

    public bool CanSave => SelectedDefinition is not null
        && ScopeError() is null
        && MissingParameters().Count == 0;

    partial void OnSelectedDefinitionChanged(ActionDefinition? value)
    {
        BuildParameters(value);
        // Choosing an action is what turns "pick one first" into a real answer, so whether the line
        // is a problem changes here as well as when a parameter is filled in.
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(ValidationIsProblem));
    }

    /// <summary>
    /// Chooses an action by its catalogue key. A palette shortcut opens this dialog on the
    /// action it stands for, so the user only has to fill the blanks in.
    /// </summary>
    public void SelectAction(string key)
    {
        if (AvailableActions.FirstOrDefault(definition => definition.Key == key) is not { } definition)
        {
            return;
        }

        SelectedDefinition = definition;
        IsPickerOpen = false;
    }

    /// <summary>Reopens the picker on the line the folded-away one left behind.</summary>
    [RelayCommand]
    private void OpenPicker()
    {
        ActionSearch = string.Empty;
        IsPickerOpen = true;
    }

    /// <summary>Folds a group open or shut and remembers which way it went.</summary>
    public void ToggleGroup(ActionGroupViewModel group)
    {
        group.IsOpen = !group.IsOpen;
        if (group.IsOpen)
        {
            _openedGroups.Add(group.Key);
        }
        else
        {
            _openedGroups.Remove(group.Key);
        }
    }

    /// <summary>What the search box does to the picker: it filters it, so it is rebuilt.</summary>
    partial void OnActionSearchChanged(string value) => RebuildActions();

    /// <summary>
    /// Fills the picker with the actions that answer to what has been typed, as a group of
    /// recently used ones, then the blocks, then a group a category.
    /// </summary>
    private void RebuildActions()
    {
        var search = ActionSearch.Trim();
        var matching = AvailableActions.Where(action => Matches(action, search)).ToList();

        // A search has already narrowed the list, so everything left in it is worth showing;
        // with a blank box the groups start folded, which is what keeps the catalogue readable.
        var searching = search.Length > 0;

        ActionGroups.Clear();

        var recent = new List<ActionDefinition>();
        foreach (var key in RecentlyUsed)
        {
            if (matching.FirstOrDefault(action => string.Equals(action.Key, key, StringComparison.Ordinal))
                is { } used && !recent.Contains(used))
            {
                recent.Add(used);
            }
        }

        if (recent.Count > 0)
        {
            Group(RecentGroup, Strings.Get("Add.RecentlyUsed"), null, recent, open: true);
        }

        // The blocks are lifted out of the categories and listed together, because "which shape
        // does this task need" is the question the picker is asked first, and the four repeats
        // only differ from each other when they stand side by side.
        var blocks = matching.Where(HoldsSteps)
            .OrderBy(BlockRank)
            .ToList();

        if (blocks.Count > 0)
        {
            Group(BlockGroup, Strings.Get("Add.Blocks"), ActionCatalog.IconFor(ActionCategory.Control),
                blocks, searching || _openedGroups.Contains(BlockGroup), Strings.Get("Add.BlocksNote"));
        }

        foreach (var category in matching.Where(action => !HoldsSteps(action))
                     .GroupBy(action => action.Category)
                     .OrderBy(group => (int)group.Key))
        {
            var key = category.Key.ToString();
            Group(key, CategoryName(category.Key), ActionCatalog.IconFor(category.Key), [.. category],
                searching || _openedGroups.Contains(key));
        }

        OnPropertyChanged(nameof(HasNoActionMatch));
    }

    /// <summary>
    /// True for an action that holds steps of its own: it is a block in the macro rather than
    /// something the run just does. The logic group is not one of these — it holds conditions,
    /// which are what a block asks about, not steps the run walks through.
    /// </summary>
    private static bool HoldsSteps(ActionDefinition action)
        => action.Parameters.Any(parameter =>
            parameter.Kind is ActionParameterKind.Steps && !parameter.ConditionsOnly);

    /// <summary>
    /// Where a block sits in <see cref="BlockOrder"/>. A block that is not on the list — the
    /// switch case, which is reached through its own parent instead — comes last rather than first.
    /// </summary>
    private static int BlockRank(ActionDefinition action)
        => Array.IndexOf(BlockOrder, action.Key) is var at && at >= 0 ? at : BlockOrder.Length;

    private void Group(string key, string title, Geometry? icon,
        IReadOnlyList<ActionDefinition> actions, bool open, string note = "")
        => ActionGroups.Add(new ActionGroupViewModel(key, title, icon, actions, open, note));

    /// <summary>True when an action answers to what has been typed: its key, name or description.</summary>
    private static bool Matches(ActionDefinition action, string search)
        => search.Length == 0
           || action.Key.Contains(search, StringComparison.OrdinalIgnoreCase)
           || action.LocalName.Contains(search, StringComparison.OrdinalIgnoreCase)
           || action.LocalDescription.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>What a category of the catalogue is called in the interface language.</summary>
    private static string CategoryName(ActionCategory category)
        => Strings.Get("Add.Category." + category, category.ToString());

    /// <summary>
    /// Notes an action as just used, moving it to the front. The list is kept short because a
    /// "recently used" group that grows without end is only another long list.
    /// </summary>
    private static void Note(ActionDefinition action)
    {
        RecentlyUsed.RemoveAll(key => string.Equals(key, action.Key, StringComparison.Ordinal));
        RecentlyUsed.Insert(0, action.Key);
        if (RecentlyUsed.Count > RecentLimit)
        {
            RecentlyUsed.RemoveRange(RecentLimit, RecentlyUsed.Count - RecentLimit);
        }
    }

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
        MetaRetryBackoff = BackoffChoices.FirstOrDefault(choice =>
            choice.Value == StepMeta.Name(step.Meta.RetryBackoff)) ?? BackoffChoices[0];

        // A step that already uses one of the folded settings opens with them in view, so the
        // reason it behaves unusually is not hidden under a fold the user has to know about.
        ShowAdvanced = Parameters.Any(parameter => parameter.IsAdvanced && !parameter.IsDefault);

        // A step being edited already has its action, so the picker folds down to the line that
        // names it and the whole dialog is about the fields.
        IsPickerOpen = false;
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

        // A different action starts folded, whatever the one before it had open.
        ShowAdvanced = false;

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
        AdvancedRows.Clear();

        FillRows(Parameters.Where(parameter => !parameter.IsAdvanced), Rows);
        FillRows(Parameters.Where(parameter => parameter.IsAdvanced), AdvancedRows);

        OnPropertyChanged(nameof(HasAdvanced));
        OnPropertyChanged(nameof(AdvancedLabel));
    }

    private static void FillRows(IEnumerable<StepParameterViewModel> parameters,
        ObservableCollection<ParameterRowViewModel> rows)
    {
        var laid = parameters.ToList();
        for (var index = 0; index < laid.Count; index++)
        {
            var current = laid[index];
            var next = index + 1 < laid.Count ? laid[index + 1] : null;

            if (current.IsRowPair && next?.IsRowPair == true
                || current.IsCoordinate && next?.IsCoordinate == true)
            {
                rows.Add(new ParameterRowViewModel(current, next));
                index++;
                continue;
            }

            rows.Add(new ParameterRowViewModel(current));
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
        OnPropertyChanged(nameof(ValidationIsProblem));
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
    /// Variable Center already created, and the variables WhaleGenie provides stay read-only.
    /// </summary>
    private string? ScopeError()
    {
        // Every action that stores a value into a named variable asks the same two questions:
        // whether the name belongs to WhaleGenie itself, and whether a shared name exists to write
        // to. Reading it off the parameters rather than listing the actions means a new one that
        // stores a value gets the same answers without being remembered here.
        if (SelectedDefinition is not { } definition
            || definition.Parameters.All(parameter => parameter.Name != "name")
            || definition.Parameters.All(parameter => parameter.Name != "scope"))
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

    /// <summary>
    /// The step as it would be saved, which is what the dialog's "test" button tries out: what is
    /// tried has to be what the step says, settings and all, rather than the fields read again
    /// somewhere else.
    /// </summary>
    internal MacroStep BuildStep() => new()
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
        },
    };

    /// <summary>Reads a whole-number box that may be left empty, counting an empty one as zero.</summary>
    private static int Whole(decimal? value) => value is null ? 0 : Math.Max(0, (int)value.Value);

    /// <summary>Opens and closes the settings the dialog keeps folded away.</summary>
    [RelayCommand]
    private void ToggleAdvanced() => ShowAdvanced = !ShowAdvanced;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        // An action that made it onto a step is one the user really reached for, which is what the
        // "recently used" group is about.
        Note(SelectedDefinition!);
        CloseRequested?.Invoke(BuildStep());
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);
}
