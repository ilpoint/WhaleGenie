using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// Fields whose hint says a variable may be written into them. A hint like that is only honest if
/// the field offers the variables, so a name can be picked instead of remembered — otherwise the
/// field is a plain box and the user is told to do something the interface does not help with.
/// </summary>
public class VariableFieldTests
{
    [Fact]
    public void A_notification_offers_the_variables_it_says_it_fills_in()
    {
        Ui.Run(() =>
        {
            var notify = Open("system.notify");
            var message = Parameter(notify, "message");
            var heading = Parameter(notify, "heading");

            // The whole value is read as one, so the expression editor may build it — this is the
            // one place where what the builder makes is exactly what the engine reads.
            Assert.True(message.OffersVariables);
            Assert.True(message.IsFormulaText);
            Assert.False(message.IsPlainText);
            Assert.NotEmpty(message.ExpressionSuggestions);
            Assert.Contains(message.ExpressionSuggestions, entry => entry == "$count");

            Assert.True(heading.OffersVariables);
            Assert.True(heading.IsFormulaText);
        });
    }

    [Fact]
    public void A_search_region_offers_the_variables_it_says_it_may_hold()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[]
                     {
                         "vision.findImage", "vision.waitImage", "vision.clickImage", "vision.findColor",
                         "ocr.findText", "condition.imageExists", "condition.imageNotExists",
                         "condition.textExists", "condition.textNotExists",
                     })
            {
                var region = Parameter(Open(key), "region");

                Assert.True(region.OffersVariables, $"{key} stopped offering its variables");
                Assert.False(region.IsPlainText, $"{key}'s region went back to a plain box");
                Assert.False(region.IsFormulaText, $"{key}'s region is not a formula field");
                Assert.NotEmpty(region.ExpressionSuggestions);

                // A region is filled in rather than worked out, so it is offered names and not the
                // expression functions: a function written there would be taken literally.
                Assert.DoesNotContain(region.ExpressionSuggestions,
                    entry => entry.EndsWith('('));
            }
        });
    }

    /// <summary>
    /// Working a value out is an action, not only a field: a macro that has numbers or text to
    /// put together picks it from the step list like any other step, which is what keeps the user
    /// from having to write a formula by hand.
    /// </summary>
    [Fact]
    public void Working_a_value_out_is_offered_as_a_step_of_its_own()
    {
        var calculate = Ui.Run(() => ActionCatalog.RunnableActions
            .FirstOrDefault(action => action.Key == "control.calculate"));

        Assert.NotNull(calculate);
        Assert.Equal(ActionCategory.Control, calculate.Category);
        Assert.Equal("变量运算", ActionStrings.Chinese["control.calculate.name"]);
    }

    /// <summary>
    /// The flags only mean something if the dialog's markup uses them, so the one place the
    /// parameter editors are written down is read here: a value field gets a list of the variables
    /// over it, and a whole-value field gets the expression editor beside it.
    /// </summary>
    [Fact]
    public void The_dialog_puts_the_variables_on_the_fields_that_take_them()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AddActionWindow.axaml"));

        var start = markup.IndexOf("IsVisible=\"{Binding OffersVariables}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the dialog stopped offering variables in a field that takes them");

        var block = markup[start..markup.IndexOf("</Grid>", start, StringComparison.Ordinal)];
        Assert.Contains("AutoCompleteBox", block, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ExpressionSuggestions}\"", block, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding IsFormulaText}\"", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnOpenExpressionBuilder\"", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every text field whose hint names variables has to offer them. The ones left out are named
    /// here with the reason, so leaving another one out by accident fails this check.
    /// </summary>
    [Fact]
    public void No_field_promising_variables_is_left_without_them()
    {
        string[] notAValue = ["control.forEach.itemVariable", "control.forEach.indexVariable",
            "file.saveVariables.names", "system.environment.name", "script.run.script"];
        string[] multiline = ["control.runMacro.arguments", "command.run.standardInput"];

        var gaps = Ui.Run(() => ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters.Select(parameter => (definition.Key, parameter)))
            .Where(row => row.parameter.Kind
                is ActionParameterKind.Text or ActionParameterKind.MultilineText)
            .Where(row => row.parameter.Hint.Contains("variable", StringComparison.OrdinalIgnoreCase)
                || row.parameter.Hint.Contains('$'))
            .Where(row => !row.parameter.AcceptsVariables && !row.parameter.AcceptsFormula)
            .Select(row => $"{row.Key}.{row.parameter.Name}")
            .Where(key => !notAValue.Contains(key) && !multiline.Contains(key))
            .Order()
            .ToList());

        Assert.Empty(gaps);
    }

    private static StepParameterViewModel Parameter(AddActionViewModel viewModel, string name)
        => viewModel.Parameters.First(parameter => parameter.Definition.Name == name);

    private static AddActionViewModel Open(string key)
    {
        // The whole catalogue is handed in, the way the dialog's own checks do it, so a condition
        // and a hidden action can be opened too.
        var viewModel = new AddActionViewModel(ActionCatalog.Definitions, ["count", "name"], []);
        viewModel.SelectAction(key);
        return viewModel;
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
