using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Viktor.Localization;
using Viktor.ViewModels;
using Viktor.Views;

namespace Viktor.Tests;

/// <summary>
/// The expression builder is there to stop a formula being written from memory: every operator is
/// a button and every variable is a list. These read back what it would put in front of the user.
/// </summary>
public class ExpressionBuilderTests
{
    [Fact]
    public void The_builder_lists_every_operator_the_language_reads()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);
            var tokens = viewModel.Operators.Select(item => item.Token).ToList();

            Assert.Contains("+", tokens);
            Assert.Contains("×", tokens);
            Assert.Contains("=", tokens);
            Assert.Contains("and", tokens);
            Assert.Contains("( )", tokens);

            // Each one says what it does, so the buttons are read rather than guessed at.
            Assert.All(viewModel.Operators, item => Assert.False(string.IsNullOrWhiteSpace(item.Hint)));
        });
    }

    [Fact]
    public void The_builder_shows_what_a_formula_comes_to()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel("1 + 2", []);

            Assert.False(viewModel.HasError);
            Assert.Equal(Strings.Format("Add.ExpressionResult", "3"), viewModel.Message);
        });
    }

    [Fact]
    public void The_builder_marks_a_formula_it_cannot_read()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel("1 +", []);

            Assert.True(viewModel.HasError);
            Assert.True(viewModel.ShowError);
        });
    }

    [Fact]
    public void The_builder_writes_a_chosen_variable_with_its_dollar_sign()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel("$count + 1", ["count", "name"])
            {
                Variable = "name",
            };

            Assert.Equal("$name", viewModel.VariableToken);
        });
    }

    [Fact]
    public void The_builder_opens_on_what_the_field_already_holds()
    {
        Ui.Run(() =>
        {
            var window = new ExpressionBuilderWindow(
                new ExpressionBuilderViewModel("$count + 1", ["count"]));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var box = window.FindControl<TextBox>("ExpressionBox");
            Assert.NotNull(box);
            Assert.Equal("$count + 1", box!.Text);
        });
    }
}
