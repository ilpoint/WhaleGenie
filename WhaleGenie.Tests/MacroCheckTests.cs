using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// What the editor can tell about a macro before it runs. The point of the check is to catch the
/// mistakes a run would otherwise report late or hide, so the cases here are the ones it decides
/// from the steps alone — and the ones it must keep quiet about.
/// </summary>
[RestoreSharedState]
public partial class MacroCheckTests
{
    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };

    private static StepParameter Steps(string name, params MacroStep[] steps)
        => new() { Name = name, Kind = ActionParameterKind.Steps, Steps = [.. steps] };

    private static StepParameter When(string name, ExpressionNode expression)
        => new() { Name = name, Kind = ActionParameterKind.Expression, Written = expression };

    private static StepParameter Text(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Text, Written = Fields.Shape(value) };

    private static StepParameter Name(string name, string value)
        => new() { Name = name, Kind = ActionParameterKind.Variable, Written = Fields.Shape(value) };

    private static StepParameter Value(string name, ExpressionNode value)
        => new() { Name = name, Kind = ActionParameterKind.Expression, Written = value };

    private static StepParameter Flag(string name, bool value)
        => new() { Name = name, Kind = ActionParameterKind.Bool, Written = Fields.Shape(value ? "true" : "false") };

    private static MacroStep Set(string name, ExpressionNode value)
        => Step("control.setVariable", Text("name", name), Text("scope", "local"), Value("value", value));

    private static MacroStep Repeat(params MacroStep[] body)
        => Step("control.repeat", Text("times", "3"), Steps("body", body));

    private static MacroStep Case(string values, params MacroStep[] body)
        => Step("control.case", Text("values", values), Steps("body", body));

    /// <summary>A call to another macro, asking for whatever of its results are named.</summary>
    private static MacroStep Call(string macro, params string[] wants)
        => Step("control.runMacro", Text("macro", macro), Results(wants));

    /// <summary>The results a step hands over, one row each, named the way the file writes them.</summary>
    /// <summary>What a call passes, one row per argument, written the way a call is written.</summary>
    private static StepParameter Arguments(params (string Name, ExpressionNode Value)[] arguments)
        => ArgumentRows(arguments, given: false);

    /// <summary>The same rows, each saying the empty value is the value it passes.</summary>
    private static StepParameter ArgumentsGiven(params (string Name, ExpressionNode Value)[] arguments)
        => ArgumentRows(arguments, given: true);

    private static StepParameter ArgumentRows(
        (string Name, ExpressionNode Value)[] arguments, bool given)
        => new()
        {
            Name = FieldNames.Arguments,
            Kind = ActionParameterKind.Arguments,
            Rows =
            [
                .. arguments.Select(argument => new StepParameterRow
                {
                    Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                    {
                        [FieldNames.Name] = Formula.Text(argument.Name),
                        [FieldNames.Value] = argument.Value,
                        [FieldNames.Given] = given ? Formula.Constant("true") : Formula.Constant("false"),
                    },
                }),
            ],
        };

    private static StepParameter Results(params string[] names)
        => Returned([.. names.Select(name => (name, name))]);

    /// <summary>Results whose engine key and destination variable are written independently.</summary>
    private static StepParameter Returned(params (string Key, string Name)[] results)
        => new()
        {
            Name = "returns",
            Kind = ActionParameterKind.Returns,
            Rows =
            [
                .. results.Select(result => new StepParameterRow
                {
                    Cells = new Dictionary<string, ExpressionNode>(System.StringComparer.OrdinalIgnoreCase)
                    {
                        ["key"] = Formula.Text(result.Key),
                        ["name"] = Formula.Text(result.Name),
                    },
                }),
            ],
        };

    /// <summary>What one macro of the project asks a call to fill in and offers to hand back.</summary>
    private static MacroCallable Callable(string name, string[]? parameters = null,
        string[]? results = null, params MacroStep[] steps)
        => new(name,
            [.. (parameters ?? []).Select(parameter => new MacroParameter { Name = parameter })],
            [.. (results ?? []).Select(result => new MacroResult { Name = result })], steps);

    /// <summary>
    /// Runs the check on the thread the headless session owns: it reads the action catalogue,
    /// whose icons are Avalonia geometries.
    /// </summary>
    private static IReadOnlyList<MacroProblem> Inspect(params MacroStep[] steps)
        => Inspect([], steps);

    /// <summary>The same with a project's shared declarations, which decide what a name means.</summary>
    private static IReadOnlyList<MacroProblem> Inspect(
        IReadOnlyList<VariableDefinition> globals, params MacroStep[] steps)
        => Ui.Run(() => MacroCheck.Inspect(steps, null, [], globals));

    private static IReadOnlyList<MacroProblem> Against(string ownName, MacroCallable[] macros,
        params MacroStep[] steps)
        => Ui.Run(() => MacroCheck.Inspect(steps, Callable(ownName), macros, []));
}
