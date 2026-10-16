using System;
using System.IO;
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
            // signature is what the button shows, and the sentence saying what the function does is
            // one hover away — names alone leave trim, slice and substring looking alike.
            Assert.Equal(WhaleGenie.Core.Expressions.Expression.Functions.Count, offered);
            Assert.All(viewModel.Functions,
                group => Assert.False(string.IsNullOrWhiteSpace(group.Header)));
            Assert.All(viewModel.Functions.SelectMany(group => group.Functions),
                choice => Assert.Equal(choice.Name + "(", choice.Insert));
            Assert.All(viewModel.Functions.SelectMany(group => group.Functions),
                choice => Assert.False(string.IsNullOrWhiteSpace(choice.Hint)));
            Assert.Contains(viewModel.Functions.SelectMany(group => group.Functions),
                choice => choice is { Name: "upper", Signature: "upper(text)" });
        });
    }

    /// <summary>
    /// The line is on the button rather than only in the view model: a sentence nobody shows is the
    /// same as no sentence, and the markup is where that is decided.
    /// </summary>
    [Fact]
    public void The_function_buttons_carry_the_line_that_explains_them()
    {
        var markup = File.ReadAllText(Path.Combine(
            Repository(), "WhaleGenie", "Views", "ExpressionBuilderWindow.axaml"));

        var start = markup.IndexOf("vm:ExpressionFunctionChoice", StringComparison.Ordinal);
        Assert.True(start >= 0, "the window stopped listing the functions");

        var block = markup[start..markup.IndexOf("</DataTemplate>", start, StringComparison.Ordinal)];
        Assert.Contains("ToolTip.Tip=\"{Binding Hint}\"", block, StringComparison.Ordinal);
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

    /// <summary>
    /// A reader who does not have the code names by heart reaches for the Chinese ones, so a button
    /// says what the function is called in Chinese with the code name beside it, and under it the
    /// very same call with every argument named in Chinese. What is written in the filter has to
    /// answer to those names too: 截取文本 is the name someone who never met `substring` would use.
    /// </summary>
    [Fact]
    public void A_function_button_says_the_name_and_how_it_is_written_in_chinese()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);

            // Every function has a name of its own to show; none falls back to the code name alone.
            Assert.All(viewModel.Functions.SelectMany(group => group.Functions), choice =>
            {
                Assert.True(choice.ShowsSignature, $"{choice.Name} has no name of its own");
                Assert.Equal($"{ExpressionText.ChineseName(choice.Function)} ({choice.Name})",
                    choice.Heading);
            });

            var substring = viewModel.Functions.SelectMany(group => group.Functions)
                .First(choice => choice.Name == "substring");
            Assert.Equal("截取文本 (substring)", substring.Heading);
            Assert.Equal("截取文本(文本, 起点 [, 长度])", substring.ChineseSignature);

            // Clicking one still writes the code, which is what the macro is made of.
            Assert.Equal("substring(", substring.Insert);

            viewModel.FunctionFilter = "截取";
            var shown = viewModel.Functions.SelectMany(group => group.Functions)
                .Select(choice => choice.Name).ToList();
            Assert.Contains("substring", shown);
            Assert.DoesNotContain("upper", shown);

            viewModel.FunctionFilter = "向下取整";
            Assert.Contains("floor", viewModel.Functions.SelectMany(group => group.Functions)
                .Select(choice => choice.Name));
        }));
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

            // A function is written out by hand on the page that is written out by hand; on the
            // structure page the same button holds what is already there instead.
            viewModel.ShowTextCommand.Execute(null);
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

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
