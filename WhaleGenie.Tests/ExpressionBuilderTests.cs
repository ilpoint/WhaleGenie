using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Converters;
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
[RestoreSharedState]
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

            // The wall opens on all of them, which is the whole table and nothing else: names alone
            // leave trim, slice and substring looking alike, so what the card cannot say — how the
            // call is written and what each argument is for — is on the bubble beside it.
            Assert.Equal(WhaleGenie.Core.Expressions.Expression.Functions.Count,
                viewModel.Functions.Count);
            Assert.All(viewModel.Functions, choice => Assert.Equal(choice.Name + "(", choice.Insert));
            Assert.All(viewModel.Functions,
                choice => Assert.False(string.IsNullOrWhiteSpace(choice.Hint)));
            Assert.All(viewModel.Functions,
                choice => Assert.False(string.IsNullOrWhiteSpace(choice.WrittenAs)));
            Assert.All(viewModel.Functions,
                choice => Assert.Equal(choice.Function.Parameters.Count, choice.Arguments.Count));
            Assert.All(viewModel.Functions.SelectMany(choice => choice.Arguments), argument =>
            {
                Assert.False(string.IsNullOrWhiteSpace(argument.Label));
                Assert.False(string.IsNullOrWhiteSpace(argument.Hint));
            });
            Assert.Contains(viewModel.Functions,
                choice => choice is { Name: "upper", Signature: "upper(text)" });
        });
    }

    /// <summary>
    /// The dock is what tells the groups apart now that no heading does, and it is the same control
    /// the variable wall narrows itself with: pressing one button leaves that group's functions and
    /// nothing else, and pressing all of them leaves the whole table.
    /// </summary>
    [Fact]
    public void The_dock_narrows_the_wall_to_one_group()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);
            var every = viewModel.FunctionFilters[0];
            var text = viewModel.FunctionFilters.First(filter => filter.Group == "text");

            // The dock says what each button is in the reader's own language, all of them first.
            Assert.Equal(Strings.Get("ExprBuilder.FunctionsAll"), every.Label);
            Assert.Equal(Strings.Get("Expr.Group.text"), text.Label);

            viewModel.SelectedFunctionFilter = text;
            Assert.Equal(
                WhaleGenie.Core.Expressions.Expression.Functions.Count(function =>
                    function.Group == "text"),
                viewModel.Functions.Count);
            Assert.All(viewModel.Functions, choice => Assert.Equal("text", choice.Group));

            viewModel.SelectedFunctionFilter = every;
            Assert.Equal(WhaleGenie.Core.Expressions.Expression.Functions.Count,
                viewModel.Functions.Count);
        });
    }

    /// <summary>
    /// A wall with nothing on it has to say so: the list is flat, so an empty one says nothing at
    /// all about which of the two questions emptied it.
    /// </summary>
    [Fact]
    public void A_wall_with_nothing_on_it_says_so()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);
            Assert.False(viewModel.HasNoFunctionMatch);

            viewModel.FunctionFilter = "没有这个函数";
            Assert.True(viewModel.HasNoFunctionMatch);

            viewModel.FunctionFilter = string.Empty;
            Assert.False(viewModel.HasNoFunctionMatch);
        });
    }

    /// <summary>
    /// How a call is written and what each argument is for are on the bubble rather than on the
    /// card: a card carrying a paragraph per function would push every other function off the wall.
    /// A sentence nobody shows is the same as no sentence, and the markup is where that is decided.
    /// </summary>
    [Fact]
    public void The_function_cards_carry_a_bubble_that_explains_them()
    {
        var markup = File.ReadAllText(Path.Combine(
            Repository(), "WhaleGenie", "Views", "ExpressionBuilderWindow.axaml"));

        var start = markup.IndexOf("vm:ExpressionFunctionChoice", StringComparison.Ordinal);
        Assert.True(start >= 0, "the window stopped listing the functions");

        var block = markup[start..markup.IndexOf("</DataTemplate>", start, StringComparison.Ordinal)];
        Assert.Contains("ToolTip.Tip", block, StringComparison.Ordinal);
        Assert.Contains("WrittenAs", block, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Arguments}\"", block, StringComparison.Ordinal);

        // The parameter suffix is gone from the card itself; it is what the bubble is for.
        Assert.DoesNotContain("ChineseSignature", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// A function keeps its group colour on the border, the way an action card keeps its category
    /// colour: the fill and the words stay neutral so the name is read before the colour.
    /// </summary>
    [Fact]
    public void The_function_cards_keep_the_group_colour_on_their_border()
    {
        Ui.Run(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);
            viewModel.SelectedFunctionFilter =
                viewModel.FunctionFilters.First(filter => filter.Group == "text");

            var window = new ExpressionBuilderWindow(viewModel);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var cards = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.DataContext is ExpressionFunctionChoice)
                .ToList();

            Assert.Equal(
                WhaleGenie.Core.Expressions.Expression.Functions.Count(
                    function => function.Group == "text"),
                cards.Count);
            Assert.All(cards, card =>
            {
                Assert.Contains("FnCard", card.Classes);
                Assert.NotNull(card.Foreground);
                Assert.Equal(
                    FunctionGroupBrushConverter.Shade(FunctionGroupBrushConverter.Hue("text"), "line"),
                    Assert.IsType<SolidColorBrush>(card.BorderBrush).Color);
            });

            // Pressing a button of the dock is what changes the wall: the selection has to travel
            // from the control back to the view model, which is the half a check on the view model
            // alone cannot see.
            var dock = window.GetVisualDescendants().OfType<ListBox>()
                .First(box => box.ItemsSource is IEnumerable<ExpressionFunctionFilter>);
            dock.SelectedItem = viewModel.FunctionFilters.First(filter => filter.Group == "dates");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("dates", viewModel.SelectedFunctionFilter?.Group);
            Assert.NotEmpty(viewModel.Functions);
            Assert.All(viewModel.Functions, choice => Assert.Equal("dates", choice.Group));

            window.Close();
        });
    }

    /// <summary>
    /// Every group of functions is painted in a hue of its own, which is what tells the groups apart
    /// now that no heading does: two groups sharing a colour would be two groups a reader cannot
    /// tell apart. A group the table has never met is painted as the last one rather than as
    /// nothing, so a function added to the language before its colour is chosen stays readable.
    /// </summary>
    [Fact]
    public void Every_function_group_is_painted_in_a_hue_of_its_own()
    {
        var groups = WhaleGenie.Core.Expressions.Expression.Functions
            .Select(function => function.Group)
            .Distinct()
            .ToList();

        var hues = groups.Select(FunctionGroupBrushConverter.Hue).ToList();
        Assert.Equal(hues.Count, hues.Distinct().Count());

        Assert.Equal(FunctionGroupBrushConverter.Hue("other"),
            FunctionGroupBrushConverter.Hue("a group the table has never met"));
        Assert.Equal(FunctionGroupBrushConverter.Hue("other"), FunctionGroupBrushConverter.Hue(null));

        // The three weights a card needs all paint something, and the writing is lighter than the
        // line it stands in, or a card would be a colour nobody can read a name off.
        var converter = new FunctionGroupBrushConverter();
        var weights = new[] { "fill", "line", "ink" }
            .Select(weight => Assert.IsType<SolidColorBrush>(converter.Convert(
                "text", typeof(IBrush), weight, CultureInfo.InvariantCulture)).Color)
            .ToList();

        Assert.All(weights, colour => Assert.True(colour.A > 0));
        Assert.True(weights[2].R > weights[0].R);
        Assert.True(weights[2].B > weights[0].B);
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

            var shown = viewModel.Functions.Select(choice => choice.Name).ToList();

            Assert.Contains("dateAdd", shown);
            Assert.Contains("formatDate", shown);
            Assert.DoesNotContain("upper", shown);
        });
    }

    /// <summary>
    /// A reader who does not have the code names by heart reaches for the Chinese ones, so a card
    /// says what the function is called in Chinese with the code name beside it, and the bubble
    /// under it the very same call with every argument named in Chinese. What is written in the
    /// filter has to answer to those names too: 截取文本 is the name someone who never met
    /// `substring` would use.
    /// </summary>
    [Fact]
    public void A_function_button_says_the_name_and_how_it_is_written_in_chinese()
    {
        Ui.Run(() => InterfaceLanguage.InChinese(() =>
        {
            var viewModel = new ExpressionBuilderViewModel(string.Empty, []);

            // Every function has a name of its own to show; none falls back to the code name alone.
            Assert.All(viewModel.Functions, choice =>
            {
                Assert.Equal($"{ExpressionText.ChineseName(choice.Function)} ({choice.Name})",
                    choice.Heading);
            });

            var substring = viewModel.Functions.First(choice => choice.Name == "substring");
            Assert.Equal("截取文本 (substring)", substring.Heading);
            Assert.Equal("截取文本(原文本, 起始位置 [, 截取长度])", substring.ChineseSignature);
            Assert.Equal(substring.ChineseSignature, substring.WrittenAs);

            // Each argument is named the way the signature names it and says what goes in it, which
            // is what the bubble puts one line each.
            Assert.Equal(["原文本", "起始位置", "截取长度"],
                substring.Arguments.Select(argument => argument.Label));
            Assert.All(substring.Arguments,
                argument => Assert.False(string.IsNullOrWhiteSpace(argument.Hint)));

            // Clicking one still writes the code, which is what the macro is made of.
            Assert.Equal("substring(", substring.Insert);

            viewModel.FunctionFilter = "截取";
            var shown = viewModel.Functions.Select(choice => choice.Name).ToList();
            Assert.Contains("substring", shown);
            Assert.DoesNotContain("upper", shown);

            viewModel.FunctionFilter = "向下取整";
            Assert.Contains("floor", viewModel.Functions.Select(choice => choice.Name));
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
