using System.Collections.Generic;
using System.Linq;
using System.Text;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One run model as the page draws it. Nothing here reads the bridge, a window or another thread:
/// the projection is a function of the model and the frames it last opened, so it can be checked
/// without a page attached.
/// </summary>
internal static class RunStateProjection
{
    /// <summary>
    /// The full state for a query or a command reply. A model that has not been loaded reads as the
    /// empty panel rather than as a missing answer, and the frames the session kept are handed in by
    /// the caller: the pictures belong to the session, not to the run model.
    /// </summary>
    public static RunState Project(RunViewModel? run, RunLook? look,
        IReadOnlyList<RunLook>? looks = null)
    {
        RunStep[] steps = [];
        RunTrigger[] triggers = [];
        RunWatch[] watches = [];
        string[] choices = [];
        if (run is { } model)
        {
            steps = [.. model.Steps.Select(step =>
            {
                var rule = model.BreakpointRuleOf(step);
                return new RunStep(
                    step.Id,
                    step.Label,
                    step.Detail,
                    step.Depth,
                    step.IsShown,
                    step.IsContainer,
                    step.IsCurrent,
                    step.HasBreakpoint,
                    step.CanBreak,
                    step.TimeNote,
                    step.FailureNote,
                    step.ConditionNote,
                    rule.HitTimes,
                    rule.WaitsForExpression ? ExpressionPhrase.Display(rule.Expression) : string.Empty,
                    step.HasRun ? step.OutcomeLabel : string.Empty,
                    WorkedOut(step));
            })];
            triggers = [.. model.Triggers];
            watches = [.. model.Variables.Select(variable => new RunWatch(
                variable.Display,
                variable.LocalType,
                variable.Value))];
            choices = [.. model.ChoiceNames];
        }

        return new RunState(
            run?.Debugged ?? string.Empty,
            run?.Status ?? string.Empty,
            run?.StatusKindValue.ToString() ?? string.Empty,
            run?.IsBusy ?? false,
            run?.IsPaused ?? false,
            run?.RunCommand.CanExecute(null) ?? false,
            run?.StepCommand.CanExecute(null) ?? false,
            run?.StopCommand.CanExecute(null) ?? false,
            run?.Question ?? string.Empty,
            run?.WantsTypedAnswer ?? false,
            run?.AnswerText ?? string.Empty,
            run?.IsAskingUser ?? false,
            run?.IsAsking ?? false,
            run?.Arguments.Select(argument => new RunArgument(
                argument.Name,
                argument.Label,
                argument.Hint,
                argument.Required,
                argument.Value,
                argument.Given,
                argument.IsMissing)).ToArray() ?? [],
            look,
            run?.Globals.Select(global => new RunGlobal(
                global.Name,
                global.LocalType,
                global.CurrentValue,
                global.DefaultValue)).ToArray() ?? [],
            run?.LocalVariables.SelectMany(group => group.Variables.Select(variable => new RunLocal(
                variable.Name,
                group.MacroName,
                variable.Type.ToString()))).ToArray() ?? [],
            steps,
            run?.Lines.Select(line => new RunLine(
                line.Time,
                line.Level.ToString(),
                line.Text)).ToArray() ?? [],
            LogFrom: 0,
            InputRate: run?.InputRate ?? 0,
            InputRateOwner: run?.InputRateOwner ?? string.Empty,
            SwapNote: run?.SwapNote ?? string.Empty,
            Triggers: triggers,
            Watches: watches,
            Looks: looks ?? [],
            ActiveLook: look?.StepId ?? string.Empty,
            BreakpointChoices: choices);
    }

    /// <summary>Changes that the page has to redraw; an unchanged tree is left off the push.</summary>
    /// <remarks>
    /// How each step ended and what it worked out are part of the tree: a step that has just run
    /// changes nothing else on its row, and a push that left them out would draw the row as it was
    /// before the step began.
    /// </remarks>
    public static string StepsSignature(IReadOnlyList<RunStep> steps)
    {
        var text = new StringBuilder();
        foreach (var step in steps)
        {
            text.Append(step.Id).Append('|')
                .Append(step.Label).Append('|')
                .Append(step.Detail).Append('|')
                .Append(step.Depth).Append('|')
                .Append(step.Shown ? '1' : '0')
                .Append(step.Container ? '1' : '0')
                .Append(step.Current ? '1' : '0')
                .Append(step.Breakpoint ? '1' : '0')
                .Append(step.CanBreak ? '1' : '0')
                .Append(step.Time).Append('|')
                .Append(step.Failure).Append('|')
                .Append(step.Condition).Append('|')
                .Append(step.HitTimes).Append('|')
                .Append(step.Expression).Append('|')
                .Append(step.Outcome).Append('|')
                .Append(step.Result).Append(';');
        }

        return text.ToString();
    }

    /// <summary>
    /// What one step worked out, as its row says it: the values it handed over, three at most, so a
    /// step that returns a long list does not push the rows under it off the panel. A step that
    /// worked nothing out says nothing rather than a dash, because most steps are of that kind.
    /// </summary>
    private static string WorkedOut(RunStepViewModel step)
    {
        if (!step.HasResults)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        foreach (var result in step.Results.Take(3))
        {
            // A record's own text is the whole of it written as JSON; its first field is the short
            // answer the row has room for.
            var value = result.Value.Length > 0
                ? result.Value
                : result.Lines.Count > 0 ? result.Lines[0].Value : string.Empty;
            parts.Add(value.Length > 0 ? $"{result.Name} = {value}" : result.Name);
        }

        return string.Join(", ", parts);
    }
}
