using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The expression builder is there to stop a formula being written from memory: every operator is
/// a button and every variable is a button too. These read back what it would put in front of the
/// user.
/// </summary>
public class ExpressionBuilderTests
{
    /// <summary>Variables of the macro's own, which is what a field is usually filled in from.</summary>
    private static IReadOnlyList<VariableChoice> Variables(params string[] names)
        => VariableChoicesForChecks.Named(names);

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
    public void The_builder_offers_a_button_for_every_variable_the_macro_knows()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel("$count + 1", Variables("count", "name"));
            var tokens = viewModel.Wall.Shown.Select(choice => choice.Token).ToList();

            Assert.Contains("$count", tokens);
            Assert.Contains("$name", tokens);
        });
    }

    [Fact]
    public void The_builder_opens_on_what_the_field_already_holds()
    {
        Ui.Run(() =>
        {
            var window = new ExpressionBuilderWindow(
                new ExpressionBuilderViewModel("$count + 1", Variables("count")));
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var box = window.FindControl<TextBox>("ExpressionBox");
            Assert.NotNull(box);
            Assert.Equal("$count + 1", box!.Text);
        });
    }

    [Fact]
    public void The_builder_offers_a_button_for_every_function_the_language_has()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);
            var offered = viewModel.Functions.Sum(group => group.Functions.Count);

            // Every function is on the list, and every one of them says how it is written: the
            // signature is the whole of what the button shows, because the description the table
            // carries is written in English and this interface never shows English.
            Assert.Equal(WhaleGenie.Core.Expressions.Expression.Functions.Count, offered);
            Assert.All(viewModel.Functions,
                group => Assert.False(string.IsNullOrWhiteSpace(group.Header)));
            Assert.All(viewModel.Functions.SelectMany(group => group.Functions),
                choice => Assert.Equal(choice.Name + "(", choice.Insert));
            Assert.Contains(viewModel.Functions.SelectMany(group => group.Functions),
                choice => choice is { Name: "upper", Signature: "upper(text)" });
        });
    }

    [Fact]
    public void The_filter_narrows_the_functions_to_what_was_typed()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, [])
            {
                FunctionFilter = "date",
            };

            var shown = viewModel.Functions.SelectMany(group => group.Functions)
                .Select(choice => choice.Name).ToList();

            Assert.Contains("dateAdd", shown);
            Assert.Contains("formatDate", shown);
            Assert.DoesNotContain("upper", shown);

            // A group whose every function was filtered out goes away with them, rather than
            // standing over an empty space.
            Assert.All(viewModel.Functions, group => Assert.NotEmpty(group.Functions));
            Assert.DoesNotContain(viewModel.Functions, group => group.Group == "text");
        });
    }

    [Fact]
    public void Clicking_a_function_leaves_its_name_ready_for_its_arguments()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);
            var window = new ExpressionBuilderWindow(viewModel);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var button = window.GetVisualDescendants().OfType<Button>()
                .First(control => control.DataContext is ExpressionFunctionChoice { Name: "upper" });
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            // The opening bracket comes with it and the caret sits after it, so what is typed next
            // is the argument rather than the rest of the name.
            var box = window.FindControl<TextBox>("ExpressionBox");
            Assert.Equal("upper(", box!.Text);
            Assert.Equal(6, box.CaretIndex);
        });
    }
}
