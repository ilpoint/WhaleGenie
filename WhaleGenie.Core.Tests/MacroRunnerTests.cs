using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

[Collection("InputGate")]
public partial class MacroRunnerTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>
    /// What a call passes: one row per argument, the way a saved step writes it. An argument with
    /// no row is one the call left out, so it takes its declared default — which is why a test that
    /// means to pass nothing writes no row at all rather than an empty one.
    /// </summary>
    private static ExecutableParameter Args(params (string Name, string Text)[] arguments)
        => new()
        {
            Name = FieldNames.Arguments,
            Rows = [.. arguments.Select(argument => Row(argument.Name, argument.Text, null))],
        };

    /// <summary>One argument row, saying whether the empty value is the value.</summary>
    private static ExecutableParameter ArgRow(string name, string text, bool? given)
        => new() { Name = FieldNames.Arguments, Rows = [Row(name, text, given)] };

    private static IReadOnlyDictionary<string, string> Row(string name, string text, bool? given)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [FieldNames.Name] = name,
            [FieldNames.Value] = text,
        };
        if (given is { } written)
        {
            row[FieldNames.Given] = written ? "true" : "false";
        }

        return row;
    }

    /// <summary>See <see cref="SampleFiles.Returns"/>: the results a step hands over.</summary>
    private static ExecutableParameter Returns(params string[] results)
        => SampleFiles.Returns(results);

    /// <summary>See <see cref="SampleFiles.Hit"/>: a place, a picture or a program and all its parts.</summary>
    private static ExecutableParameter Hit(string key, string name) => SampleFiles.Hit(key, name);

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => new() { Name = name, Steps = steps };

    private static ExecutableParameter When(string name, ExecutableStep step)
        => new() { Name = name, Text = TestConditions.Expression(step) };

    private static ExecutableStep Set(string name, string value)
        => Step("control.setVariable", Param("name", name), Param("scope", "local"), Param("value", value));

    private static ExecutableStep AddOne(string name) => Set(name, "$" + name + " + 1");

    /// <summary>
    /// A step that fails however it is run: a key press with no device layer behind it reports
    /// that the keyboard is missing. Tests say what the macro makes of a failure with it instead
    /// of leaning on a real machine.
    /// </summary>
    private static ExecutableStep Broken(StepMeta? meta = null)
        => new()
        {
            Type = "input.keyPress",
            Parameters = [Param("key", "F5")],
            Meta = meta ?? StepMeta.Empty,
        };

    private static VariableStore Store(params (string Name, double Value)[] numbers)
    {
        var store = new VariableStore();
        foreach (var (name, value) in numbers)
        {
            store.Local.SetNumber(name, value);
        }

        return store;
    }

    private static double N(VariableStore variables, string name)
        => variables.Local.Values[name].AsNumber();

    private static async Task<(RunResult Result, SilentRunHost Host)> RunAsync(
        ExecutableStep[] steps, IRunHost? host = null, VariableStore? variables = null,
        IMacroLibrary? macros = null, string runName = "", CancellationToken token = default,
        MacroSignature? startSignature = null,
        IReadOnlyList<MacroStartArgument>? startArguments = null)
    {
        var store = variables ?? Store();
        var silent = host as SilentRunHost ?? new SilentRunHost();
        var result = await new MacroRunner(store, host ?? silent, null, 1, macros)
        {
            RunName = runName,
            StartSignature = startSignature,
            StartArguments = startArguments,
        }.RunAsync(steps, token);
        return (result, silent);
    }

    /// <summary>Runs the steps at the given speed and reports how long they took.</summary>
    private static async Task<double> TimeAsync(ExecutableStep[] steps, double delayScale)
    {
        var watch = Stopwatch.StartNew();
        var result = await new MacroRunner(new VariableStore(), new SilentRunHost(),
            NullDeviceLayer.Instance, delayScale).RunAsync(steps);
        watch.Stop();

        Assert.True(result.Succeeded, result.Key);
        return watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>
    /// The shortest of several runs at the given speed. A busy machine can only ever add time to
    /// a wait, so the shortest run is the one that shows what the macro really asked for: a run
    /// that happened to be scheduled late would otherwise fail an honest pause.
    /// </summary>
    private static async Task<double> QuickestAsync(ExecutableStep[] steps, double delayScale,
        int rounds = 5)
    {
        var shortest = double.MaxValue;
        for (var round = 0; round < rounds; round++)
        {
            shortest = Math.Min(shortest, await TimeAsync(steps, delayScale));
        }

        return shortest;
    }

    /// <summary>
    /// Holds a set of waits to the length they were written as. Every round has to reach the short
    /// end — no wait ever comes back before it was asked for — while the far end is read off the
    /// quickest round, because a busy machine can only ever add to a wait: a round that happened to
    /// be held up says nothing about the number the macro was written with.
    /// </summary>
    private static void AssertWaitsAreSpaced(List<double> waits, double shortest, double longest)
    {
        Assert.All(waits, taken => Assert.True(taken >= shortest,
            $"waited {taken:0}ms, before the short end"));
        Assert.True(waits.Min() <= longest,
            $"the quickest of {waits.Count} waits was {waits.Min():0}ms, past the long end");
    }
}
