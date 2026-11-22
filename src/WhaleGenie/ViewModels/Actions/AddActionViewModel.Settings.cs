using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// The settings around a step: the branch list, the retry plan and the failure rule.
public partial class AddActionViewModel
{
    /// <summary>The page on screen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OnBase))]
    [NotifyPropertyChangedFor(nameof(OnExec))]
    [NotifyPropertyChangedFor(nameof(OnAdvanced))]
    [NotifyPropertyChangedFor(nameof(OnReturns))]
    [NotifyPropertyChangedFor(nameof(OnOther))]
    public partial ActionPageViewModel? Page { get; set; }

    /// <summary>True while the page about what the step is is the one on screen.</summary>
    public bool OnBase => Page?.Key == BasePage;

    /// <summary>True while the page of settings every step has is on screen.</summary>
    public bool OnExec => Page?.Key == ExecPage;

    /// <summary>True while the page of settings for another approach is on screen.</summary>
    public bool OnAdvanced => Page?.Key == AdvancedPage;

    /// <summary>True while the page of results the step may hand over is on screen.</summary>
    public bool OnReturns => Page?.Key == ReturnsPage;

    /// <summary>True while the page of step settings and the JSON is on screen.</summary>
    public bool OnOther => Page?.Key == OtherPage;

    /// <summary>The parameters as the dialog shows them, with a position's x and y on one line.</summary>
    public ObservableCollection<ParameterRowViewModel> Rows { get; } = [];

    /// <summary>The settings whose answer is "how this step does it", as another list of lines.</summary>
    public ObservableCollection<ParameterRowViewModel> AdvancedRows { get; } = [];

    /// <summary>The results this step may hand over, which are asked for on their own page.</summary>
    public ObservableCollection<ReturnsRowViewModel> ReturnsRows { get; } = [];

    /// <summary>
    /// What this step passes to the macro it calls, one row per argument the called macro declares.
    /// It is filled from that macro rather than from the catalogue, so it is not a row of the
    /// ordinary parameter list: the names change when the call names another macro.
    /// </summary>
    public ObservableCollection<CallArgumentRowViewModel> CallArgumentRows { get; } = [];

    /// <summary>True when the macro being called declares something this step can fill in.</summary>
    public bool HasCallArguments => CallArgumentRows.Count > 0;

    /// <summary>
    /// The branches of a switch. They are steps rather than fields, so they sit in the editor's
    /// list and are dragged into place there; this page only counts them and offers the one thing
    /// the list cannot: a way to add one without leaving the form. The block is the only place a
    /// branch order is read, so the count belongs here beside the value they are compared with.
    /// </summary>
    public bool ShowsBranches => SelectedDefinition?.Key is "control.switch";

    /// <summary>How many branches there are, and how much the fallback runs.</summary>
    public string BranchSummary
    {
        get
        {
            var cases = Parameter("cases")?.List?.Steps.Count ?? 0;
            var otherwise = Parameter("otherwise")?.List?.Steps.Count ?? 0;
            return Strings.Format("Add.BranchSummary", cases, otherwise);
        }
    }

    /// <summary>
    /// The three lists of a try block. They are steps rather than fields, so this page cannot show
    /// their contents — only how much sits in each. Without that sentence the page looks like a
    /// step with one condition and no way to see what it is actually protecting.
    /// </summary>
    public bool ShowsAttempt => SelectedDefinition?.Key is "control.try";

    /// <summary>How much sits in each of the three lists.</summary>
    public string AttemptSummary
    {
        get
        {
            var tried = Parameter("body")?.List?.Steps.Count ?? 0;
            var caught = Parameter("catch")?.List?.Steps.Count ?? 0;
            var always = Parameter("finally")?.List?.Steps.Count ?? 0;
            return Strings.Format("Add.AttemptSummary", tried, caught, always);
        }
    }

    /// <summary>Adds the next branch, which starts empty and is filled in on the editor's list.</summary>
    [RelayCommand]
    private void AddBranch()
    {
        if (Parameter("cases") is not { List: { } list })
        {
            return;
        }

        // The list the step goes back into is the editor's, so the branch has to be named the way
        // the editor names steps. The generator is handed in by the editor; a form opened with
        // nothing behind it still names its branches, out of the names it has handed out itself,
        // because a branch with no name is one a condition could never point at.
        var id = NextStepId?.Invoke(_branchIds) ?? StepIds.Next(_branchIds);
        _branchIds.Add(id);

        list.Steps.Add(new MacroStep
        {
            Type = "control.case",
            Id = id,
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter { Name = "values", Kind = ActionParameterKind.Text },
                new StepParameter { Name = "body", Kind = ActionParameterKind.Steps },
            ],
        });
        OnPropertyChanged(nameof(BranchSummary));
        OnPropertyChanged(nameof(JsonPreview));
    }

    /// <summary>
    /// The ids this form has handed out, so two branches added in one visit are not given the same
    /// name: the macro's own list of names does not hold them until the form is saved.
    /// </summary>
    private readonly HashSet<string> _branchIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Where a step written here asks for a name no step in the macro is using.</summary>
    public Func<ISet<string>, string>? NextStepId { get; set; }

    /// <summary>True when this action has settings that belong on the advanced page.</summary>
    public bool HasAdvanced => AdvancedRows.Any(row => row.IsApplicable);

    /// <summary>True when this action can hand anything over.</summary>
    public bool HasReturns => ReturnsRows.Count > 0;

    /// <summary>The field that holds the results of this action, or null for one that leaves none.</summary>
    private StepParameterViewModel? ReturnsField
        => Parameters.FirstOrDefault(parameter => parameter.IsReturns);

    public bool HasSelection => SelectedDefinition is not null;

    public bool HasParameters => Rows.Any(row => row.IsApplicable);

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
    [NotifyPropertyChangedFor(nameof(TunedStep))]
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
    [NotifyPropertyChangedFor(nameof(IsStopRule))]
    [NotifyPropertyChangedFor(nameof(IsContinueRule))]
    [NotifyPropertyChangedFor(nameof(IsNextRoundRule))]
    [NotifyPropertyChangedFor(nameof(IsAskRule))]
    [NotifyPropertyChangedFor(nameof(TunedStep))]
    public partial ActionParameterOption MetaOnError { get; set; }

    /// <summary>Choices offered by the failure dropdown.</summary>
    public IReadOnlyList<ActionParameterOption> ErrorChoices { get; } =
    [
        new("stop", Strings.Get("Add.OnError.Stop")),
        new("continue", Strings.Get("Add.OnError.Continue")),
        new("nextIteration", Strings.Get("Add.OnError.NextIteration")),
        new("ask", Strings.Get("Add.OnError.AskUser")),
    ];

    /// <summary>
    /// The failure rules as four cards rather than a dropdown. A dropdown of four one-line choices
    /// hides what each one does to the rest of the macro, and the difference between them is the
    /// difference between a step that stops the run and one that is allowed to miss.
    /// </summary>
    public bool IsStopRule
    {
        get => Picked(MetaOnError, "stop");
        set => PickError(value, "stop");
    }

    /// <summary>True while "carry on with the next step" is the rule.</summary>
    public bool IsContinueRule
    {
        get => Picked(MetaOnError, "continue");
        set => PickError(value, "continue");
    }

    /// <summary>True while "skip to the next loop round" is the rule.</summary>
    public bool IsNextRoundRule
    {
        get => Picked(MetaOnError, "nextIteration");
        set => PickError(value, "nextIteration");
    }

    /// <summary>True while "ask me" is the rule, which is the one that needs saying more about.</summary>
    public bool IsAskRule
    {
        get => Picked(MetaOnError, "ask");
        set => PickError(value, "ask");
    }

    /// <summary>
    /// True when this step carries a timeout, a retry, a pause or a failure rule that is not the
    /// one every step starts with. That is what the page's tab mark is read from, and it is the
    /// same question <c>StepMeta.IsEmpty</c> asks before it writes anything to the file.
    /// </summary>
    public bool TunedStep => MetaTimeout.Milliseconds > 0
        || Whole(MetaRetryCount) > 0
        || MetaDelayBefore.Milliseconds > 0
        || MetaDelayAfter.Milliseconds > 0
        || !Picked(MetaOnError, "stop");

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
}
