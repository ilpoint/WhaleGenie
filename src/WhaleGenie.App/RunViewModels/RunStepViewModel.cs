using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// One row of the step list: what the step is, where it sits, and what it did.
/// <summary>One step in the debugger's list.</summary>
public partial class RunStepViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string Detail { get; init; }

    /// <summary>How deep the step sits, used to indent it under its parent.</summary>
    public required int Depth { get; init; }

    /// <summary>
    /// One entry for every level above this step, drawn as a guide line: the list is flat, and the
    /// lines are what make a step inside a loop look like one.
    /// </summary>
    public IReadOnlyList<int> Guides => [.. Enumerable.Range(0, Depth)];

    /// <summary>True for a step that holds steps of its own: a group, a loop, a branch.</summary>
    public bool IsContainer { get; init; }

    /// <summary>The step this one sits inside, or nothing for a step at the top of the macro.</summary>
    public RunStepViewModel? Parent { get; set; }

    /// <summary>The steps this one holds, in the order they run.</summary>
    public List<RunStepViewModel> Children { get; } = [];

    /// <summary>True while the steps inside this one are shown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Marker))]
    public partial bool IsExpanded { get; set; } = true;

    /// <summary>True when nothing above this step is folded away.</summary>
    [ObservableProperty]
    public partial bool IsShown { get; set; } = true;

    /// <summary>The mark in front of a container: folded or open, it says the row holds steps.</summary>
    public string Marker => IsContainer ? (IsExpanded ? "▾" : "▸") : string.Empty;

    /// <summary>What the action is for, in the catalogue's own words.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>The action's own key, which is what the file and the log call it by.</summary>
    public string ActionKey { get; init; } = string.Empty;

    /// <summary>
    /// The bubble over the row: the whole note the step carries and what the action does, which is
    /// more than the row has room for.
    /// </summary>
    public string Tooltip
    {
        get
        {
            var lines = new List<string> { Label };
            if (Description.Length > 0)
            {
                lines.Add(Description);
            }

            if (Comment.Length > 0)
            {
                lines.Add($"备注：{Comment}");
            }

            lines.Add(Detail);
            lines.Add(string.Join(" · ", new[] { Id, ActionKey }.Where(part => part.Length > 0)));
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>The note the step carries, or empty when it carries none.</summary>
    public string Comment { get; init; } = string.Empty;

    public bool HasComment => Comment.Length > 0;

    /// <summary>
    /// Whether the engine can stop in front of this row. A condition is asked by the step it
    /// belongs to rather than run on its own, so nothing pauses before one: a dot on that row would
    /// be a promise the run cannot keep, and stopping before the question is asked is what the dot
    /// on the step it belongs to is for.
    /// </summary>
    public required bool CanBreak { get; init; }

    [ObservableProperty]
    public partial bool HasBreakpoint { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>True once this step has something to show for itself: what it last looked at.</summary>
    [ObservableProperty]
    public partial bool HasLook { get; set; }

    /// <summary>True once this step has run, so the panel under it has something to say.</summary>
    [ObservableProperty]
    public partial bool HasRun { get; set; }

    /// <summary>How the step ended, in the interface's own words.</summary>
    [ObservableProperty]
    public partial string OutcomeLabel { get; set; } = string.Empty;

    /// <summary>The colour that ending reads in, the way a step's outcome is coloured elsewhere.</summary>
    [ObservableProperty]
    public partial string OutcomeColor { get; set; } = "#9A9A9A";

    /// <summary>How long the step took, written the way the interface writes a length of time.</summary>
    [ObservableProperty]
    public partial string TimeNote { get; set; } = string.Empty;

    /// <summary>Why the step failed, or empty when it did not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    public partial string FailureNote { get; set; } = string.Empty;

    /// <summary>True when this step has something to say about why it failed.</summary>
    public bool HasFailure => FailureNote.Length > 0;

    /// <summary>The condition this breakpoint waits for, or empty while it always stops.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCondition))]
    public partial string ConditionNote { get; set; } = string.Empty;

    public bool HasCondition => ConditionNote.Length > 0;

    /// <summary>
    /// Everything the step worked out, read-only: what the macro keeps and what it does not. Ticking
    /// a result decides whether it becomes a variable, never whether it can be read here.
    /// </summary>
    public ObservableCollection<RunResultViewModel> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    /// <summary>Says how many values the step worked out.</summary>
    public string ResultsHeader => Strings.Format("Run.StepResults", Results.Count);

    /// <summary>Every try the step made, oldest first.</summary>
    public ObservableCollection<RunAttemptViewModel> Attempts { get; } = [];

    public bool HasAttempts => Attempts.Count > 0;

    /// <summary>Says how many tries the step needed.</summary>
    public string AttemptsHeader => Strings.Format("Run.StepAttempts", Attempts.Count);

    /// <summary>
    /// Takes what this step came to: how it ended, how long it took, why if it failed, and every
    /// value it worked out. A step that ran more than once — a loop, a retry — is answered by the
    /// last time round, which is what its row is about.
    /// </summary>
    public void Took(StepRun run)
    {
        HasRun = true;
        OutcomeLabel = Strings.Get(run.Outcome switch
        {
            "failed" => "Run.OutcomeFailed",
            "skipped" => "Run.OutcomeSkipped",
            _ => "Run.OutcomeOk",
        });
        OutcomeColor = run.Outcome switch
        {
            "failed" => "#E06C75",
            "ok" => "#8CC98C",
            _ => "#9A9A9A",
        };
        TimeNote = DurationUnit.Written(Math.Round((decimal)run.ElapsedMs));
        // The key is what a condition branches on, so the row shows it beside the words rather
        // than making the reader find the matching variable and compare by eye.
        var reason = run.FailureDetail.Length == 0
            ? Strings.Get(run.FailureKey)
            : Strings.Format(run.FailureKey, run.FailureDetail);
        FailureNote = run.FailureKey.Length == 0
            ? string.Empty
            : Strings.Format("Run.FailureNote", run.FailureKey, reason);

        Attempts.Clear();
        foreach (var attempt in run.Attempts)
        {
            var left = attempt.RemainingMs is { } remaining
                ? Strings.Format("Run.AttemptLeft",
                    DurationUnit.Written(Math.Round((decimal)remaining)))
                : string.Empty;
            var note = Strings.Format("Run.AttemptLine", attempt.Number,
                DurationUnit.Written(Math.Round((decimal)attempt.ElapsedMs)), left);

            var failure = attempt.FailureKey.Length == 0
                ? string.Empty
                : Strings.Format("Run.AttemptFailed", attempt.FailureKey);
            if (attempt.Overran)
            {
                var late = Strings.Get("Run.AttemptOverran");
                failure = failure.Length == 0 ? late : $"{failure} {late}";
            }

            Attempts.Add(new RunAttemptViewModel(note, failure, failure.Length > 0));
        }

        OnPropertyChanged(nameof(HasAttempts));
        Results.Clear();
        foreach (var result in run.Results)
        {
            // A result the macro keeps reads as the name it goes by; one nobody ticked reads as the
            // engine's own name for it, which is what there is to call it.
            var kept = result.Name.Length > 0;
            var fields = result.Data is { } data ? RunResultLines.Of(JsonTreeNode.Fields(data)) : [];
            Results.Add(new RunResultViewModel(kept ? result.Name : result.Key,
                fields.Count == 0 ? result.Text : string.Empty, kept)
            {
                // A record's own text is the whole of it written as JSON, which is longer and less
                // readable than the fields it is made of: the fields are what the row shows.
                Lines = fields,
            });
        }

        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>
    /// Keeps what the step's breakpoint waits for, so the row says why a run may go past it: a
    /// breakpoint with a condition is not the same promise as one without.
    /// </summary>
    public void Waits(BreakpointRule? rule)
    {
        ConditionNote = rule is null || rule.Always ? string.Empty : rule.Note;
    }
}
