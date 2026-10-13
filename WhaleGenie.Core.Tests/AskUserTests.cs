using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The two steps that put the macro's own question to whoever is at the machine, and what a macro
/// reads when there was nobody to ask.
/// </summary>
public class AskUserTests
{
    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Id = "aaaa", Parameters = parameters };

    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>See <see cref="SampleFiles.Returns"/>: the results a step hands over.</summary>
    private static ExecutableParameter Returns(params string[] results)
        => SampleFiles.Returns(results);

    private static async Task<(VariableStore Store, AnsweringHost Host)> RunAsync(
        ExecutableStep[] steps, AnsweringHost? host = null)
    {
        var store = new VariableStore();
        var answering = host ?? new AnsweringHost();
        await new MacroRunner(store, answering, null, 1).RunAsync(steps);
        return (store, answering);
    }

    [Fact]
    public async Task A_question_reads_as_confirmed_when_the_person_says_yes()
    {
        var (store, host) = await RunAsync(
        [
            Step("control.ask", Param("message", "Go on?"), Returns("confirmed=go")),
        ], new AnsweringHost(new AskAnswer(true, string.Empty)));

        Assert.True(store.Local.Values["go"].AsBool());
        Assert.Equal(AskKind.Confirm, host.Asked[0].Kind);
        Assert.Equal("Go on?", host.Asked[0].Message);
    }

    [Fact]
    public async Task A_question_reads_as_refused_when_the_person_says_no()
    {
        var (store, _) = await RunAsync(
        [
            Step("control.ask", Param("message", "Go on?"), Returns("confirmed=go")),
        ], new AnsweringHost(new AskAnswer(false, string.Empty)));

        Assert.False(store.Local.Values["go"].AsBool());
    }

    [Fact]
    public async Task The_detail_is_shown_under_the_question()
    {
        var (_, host) = await RunAsync(
        [
            Step("control.ask", Param("message", "Ready?"), Param("detail", "About to overwrite"),
                Returns("confirmed=go")),
        ]);

        Assert.Equal("Ready?\n\nAbout to overwrite", host.Asked[0].Message);
    }

    [Fact]
    public async Task A_question_with_nobody_to_ask_reads_as_no_and_the_step_still_works()
    {
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), null, 1)
            .RunAsync([Step("control.ask", Param("message", "Go on?"), Returns("confirmed=go"))]);

        Assert.True(result.Succeeded);
        Assert.False(store.Local.Values["go"].AsBool());
    }

    [Fact]
    public async Task Typed_in_writing_is_kept_as_the_answer()
    {
        var (store, host) = await RunAsync(
        [
            Step("control.input", Param("message", "Order number?"), Param("default", "A1001"),
                Returns("text=order")),
        ], new AnsweringHost(new AskAnswer(true, "A2048")));

        Assert.Equal("A2048", store.Local.Values["order"].AsText());
        Assert.Equal(AskKind.Text, host.Asked[0].Kind);
        Assert.Equal("A1001", host.Asked[0].Default);
    }

    [Fact]
    public async Task A_writing_question_with_nobody_to_ask_reads_as_empty()
    {
        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), null, 1)
            .RunAsync([Step("control.input", Param("message", "Order number?"), Returns("text=order"))]);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, store.Local.Values["order"].AsText());
    }

    [Fact]
    public async Task A_question_with_no_words_written_is_refused()
    {
        var store = new VariableStore();
        await new MacroRunner(store, new SilentRunHost(), null, 1).RunAsync(
        [
            Step("control.ask", Returns("confirmed=go")),
            Step("control.setVariable", Param("name", "after"), Param("scope", "local"),
                Param("value", "ran")),
        ]);

        Assert.False(store.Local.Values.ContainsKey("go"));
        Assert.False(store.Local.Values.ContainsKey("after"));
    }

    /// <summary>Answers whatever it is asked, and writes down every question it was put.</summary>
    private sealed class AnsweringHost(params AskAnswer[] answers) : IRunHost
    {
        private int _index;

        public List<AskQuestion> Asked { get; } = [];

        public void Log(LogEntry entry)
        {
        }

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromResult(true);

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
        {
            Asked.Add(question);
            var answer = _index < answers.Length ? answers[_index] : AskAnswer.Cancel;
            _index++;
            return Task.FromResult(answer);
        }
    }
}
