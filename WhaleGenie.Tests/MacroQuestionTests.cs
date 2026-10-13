using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The two steps that ask the person something: how the question reaches a window, and what the
/// run window does with it while the macro waits.
/// </summary>
public class MacroQuestionTests
{
    [Fact]
    public async Task A_triggered_run_gets_its_answer_through_the_window()
    {
        var asked = new List<AskQuestion>();
        var host = new AskRunHost(null, question =>
        {
            asked.Add(question);
            return Task.FromResult(new AskAnswer(true, "A2048"));
        });

        var answer = await host.AskUser(
            new AskQuestion(AskKind.Text, "Order number?", "A1001"), CancellationToken.None);

        Assert.True(answer.Confirmed);
        Assert.Equal("A2048", answer.Text);
        var question = Assert.Single(asked);
        Assert.Equal(AskKind.Text, question.Kind);
        Assert.Equal("Order number?", question.Message);
        Assert.Equal("A1001", question.Default);
    }

    [Fact]
    public async Task With_nobody_to_ask_a_question_reads_as_unanswered()
    {
        var host = new AskRunHost(null);

        var answer = await host.AskUser(
            new AskQuestion(AskKind.Confirm, "Go on?", string.Empty), CancellationToken.None);

        Assert.False(answer.Confirmed);
        Assert.Equal(string.Empty, answer.Text);
    }

    [Fact]
    public async Task A_run_that_has_been_stopped_does_not_ask()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var asked = false;
        var host = new AskRunHost(null, _ =>
        {
            asked = true;
            return Task.FromResult(new AskAnswer(true, "yes"));
        });

        var answer = await host.AskUser(
            new AskQuestion(AskKind.Confirm, "Go on?", string.Empty), cancelled.Token);

        Assert.False(asked);
        Assert.False(answer.Confirmed);
    }

    [Fact]
    public async Task The_run_window_shows_the_question_and_hands_the_typing_back()
    {
        var (asking, typed, answered, stillAsking) = Ui.RunAsync(async () =>
        {
            var viewModel = new RunViewModel([], NullDeviceLayer.Instance);
            var waiting = viewModel.AskUser(
                new AskQuestion(AskKind.Text, "Order number?", "A1001"), CancellationToken.None);

            var onScreen = (viewModel.IsAskingUser, viewModel.WantsTypedAnswer, viewModel.Question,
                viewModel.AnswerText);
            viewModel.AnswerText = "A2048";
            viewModel.AnswerUserCommand.Execute("ok");
            var answer = await waiting;

            return (onScreen, answer.Text, answer.Confirmed, viewModel.IsAskingUser);
        });

        Assert.Equal((true, true, "Order number?", "A1001"), asking);
        Assert.Equal("A2048", typed);
        Assert.True(answered);
        Assert.False(stillAsking);
    }

    [Fact]
    public async Task Turning_a_question_down_reads_as_unanswered()
    {
        var confirmed = Ui.RunAsync(async () =>
        {
            var viewModel = new RunViewModel([], NullDeviceLayer.Instance);
            var waiting = viewModel.AskUser(
                new AskQuestion(AskKind.Confirm, "Go on?", string.Empty), CancellationToken.None);

            viewModel.AnswerUserCommand.Execute("cancel");
            var answer = await waiting;

            return answer.Confirmed;
        });

        Assert.False(confirmed);
    }

    [Fact]
    public void The_prompt_dialog_hands_back_what_is_in_the_box()
    {
        var viewModel = new TextPromptViewModel { Header = "WhaleGenie", Question = "Order?", Answer = "A1" };
        string? handed = "not asked";
        viewModel.CloseRequested += answer => handed = answer;

        viewModel.OkCommand.Execute(null);
        Assert.Equal("A1", handed);

        viewModel.CancelCommand.Execute(null);
        Assert.Null(handed);
    }
}
