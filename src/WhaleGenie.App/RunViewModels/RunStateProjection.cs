using System.Collections.Generic;
using System.Linq;
using System.Text;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One run model as the page draws it. Nothing here reads the bridge, a window or another thread:
/// the projection is a function of the model and the frame it last opened, so it can be checked
/// without a page attached.
/// </summary>
internal static class RunStateProjection
{
    /// <summary>
    /// The full state for a query or a command reply. A model that has not been loaded reads as the
    /// empty panel rather than as a missing answer.
    /// </summary>
    public static RunState Project(RunViewModel? run, RunLook? look)
    {
        RunStep[] steps = [];
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
                    rule.WaitsForExpression ? ExpressionPhrase.Display(rule.Expression) : string.Empty);
            })];
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
                line.Text)).ToArray() ?? []);
    }

    /// <summary>Changes that the page has to redraw; an unchanged tree is left off the push.</summary>
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
                .Append(step.Expression).Append(';');
        }

        return text.ToString();
    }
}
