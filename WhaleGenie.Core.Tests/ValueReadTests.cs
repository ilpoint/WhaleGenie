using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A field's read mode is what decides whether a bare name is a variable, an expression or text.
/// These checks use ordinary steps so the answer is what a run would actually do.
/// </summary>
public class ValueReadTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    private static async Task<(RunResult Result, FakeDeviceLayer Devices)> RunAsync(
        ExecutableStep step, params (string Name, string Value)[] variables)
    {
        var store = new VariableStore();
        foreach (var (name, value) in variables)
        {
            store.Local.SetText(name, value);
        }

        var devices = new FakeDeviceLayer();
        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync([step]);
        return (result, devices);
    }

    [Fact]
    public async Task A_text_field_fills_a_name_in_where_it_stands()
    {
        var (result, devices) = await RunAsync(
            Step("file.listFiles", Param("folder", "."), Param("pattern", "$name-*.txt")),
            ("name", "notes"));

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains(devices.Calls,
            call => call.Contains("notes-*.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_text_field_leaves_a_doubled_dollar_as_one_dollar()
    {
        var (result, devices) = await RunAsync(
            Step("file.listFiles", Param("folder", "."), Param("pattern", "$$name-*.txt")),
            ("name", "notes"));

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains(devices.Calls,
            call => call.Contains("$name-*.txt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_whole_value_field_reads_a_bare_name_as_text()
    {
        var store = new VariableStore();
        store.Local.SetNumber("total", 5);

        var result = await new MacroRunner(store, new SilentRunHost(), new FakeDeviceLayer())
            .RunAsync([
                Step("control.setVariable", Param("name", "answer"), Param("scope", "local"),
                    Param("value", "total")),
            ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.TryGet("answer", out var answer));
        Assert.Equal("total", answer.AsText());
    }

    [Fact]
    public async Task A_whole_value_field_reads_a_doubled_dollar_as_one_dollar()
    {
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), new FakeDeviceLayer())
            .RunAsync([
                Step("control.setVariable", Param("name", "answer"), Param("scope", "local"),
                    Param("value", "$$100 off")),
            ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.TryGet("answer", out var answer));
        Assert.Equal("$100 off", answer.AsText());
    }

    [Fact]
    public async Task A_number_field_does_not_turn_a_bare_name_into_a_number()
    {
        var store = new VariableStore();
        store.Local.SetNumber("total", 5);

        var result = await new MacroRunner(store, new SilentRunHost(), new FakeDeviceLayer())
            .RunAsync([
                Step("control.repeat", Param("times", "total")),
            ]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadExpression, result.Key);
    }

    [Fact]
    public async Task A_bare_name_field_still_reads_the_variable_it_names()
    {
        var store = new VariableStore();
        store.Local.SetNumber("total", 7);

        var result = await new MacroRunner(store, new SilentRunHost(), new FakeDeviceLayer())
            .RunAsync([
                Step("control.if",
                    Param("condition", "$total == 7"),
                    new ExecutableParameter
                    {
                        Name = "then",
                        Steps = [Step("control.setVariable", Param("name", "seen"),
                            Param("scope", "local"), Param("value", "yes"))],
                    }),
            ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.TryGet("seen", out var seen));
        Assert.Equal("yes", seen.AsText());
    }

    /// <summary>
    /// A condition written with one "=" is a typo for "==", and it has to be told as one. Read as
    /// the words "1 = 2" it is a non-empty text, which is true — so the branch the author meant to
    /// skip ran, and nothing anywhere said why.
    /// </summary>
    [Fact]
    public async Task A_single_equals_in_a_condition_is_a_failure_not_a_true()
    {
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), new FakeDeviceLayer())
            .RunAsync([
                Step("control.if",
                    Param("condition", "1 = 2"),
                    new ExecutableParameter
                    {
                        Name = "then",
                        Steps = [Step("control.setVariable", Param("name", "ran"),
                            Param("scope", "local"), Param("value", "yes"))],
                    }),
            ]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadExpression, result.Key);
        Assert.False(store.TryGet("ran", out _));
    }

    /// <summary>
    /// Words are still a value: a condition that names no comparison keeps the meaning it had, so
    /// the fix above takes nothing away from a macro that wrote one.
    /// </summary>
    [Fact]
    public async Task A_condition_in_words_still_means_what_it_said()
    {
        var store = new VariableStore();
        store.Local.SetText("state", "已登录");

        var result = await new MacroRunner(store, new SilentRunHost(), new FakeDeviceLayer())
            .RunAsync([
                Step("control.if",
                    Param("condition", "$state"),
                    new ExecutableParameter
                    {
                        Name = "then",
                        Steps = [Step("control.setVariable", Param("name", "ran"),
                            Param("scope", "local"), Param("value", "yes"))],
                    }),
            ]);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.TryGet("ran", out _));
    }
}
