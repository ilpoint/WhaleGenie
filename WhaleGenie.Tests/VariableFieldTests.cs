using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
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
            var message = Parameter(notify, "notificationText");
            var heading = Parameter(notify, "heading");

            // The whole value is read as one, so the expression editor may build it — this is the
            // one place where what the builder makes is exactly what the engine reads.
            Assert.True(message.OffersVariables);
            Assert.True(message.OffersFormula);
            Assert.False(message.IsPlainText);
            Assert.NotEmpty(message.ExpressionSuggestions);
            Assert.Contains(message.ExpressionSuggestions, entry => entry == "$count");

            Assert.True(heading.OffersVariables);
            Assert.True(heading.OffersFormula);
        });
    }

    [Fact]
    public void A_declared_record_parameter_is_offered_to_a_record_field()
    {
        Ui.Run(() =>
        {
            var declared = VariableChoices.For([],
                [new MacroParameter { Name = "窗口", Type = ActionReturnType.Record }],
                []);
            var viewModel = new AddActionViewModel(ActionCatalog.Definitions, declared, []);
            viewModel.SelectAction("input.mouseMove");

            var place = Parameter(viewModel, "place");

            Assert.Contains("$窗口", place.ExpressionSuggestions);
        });
    }

    [Fact]
    public void A_search_region_is_a_list_of_places_rather_than_a_line_of_punctuation()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[]
                     {
                         "vision.findImage", "vision.waitImage", "vision.clickImage", "vision.findColor",
                         "ocr.findText",
                     })
            {
                var region = Parameter(Open(key), "region");

                Assert.True(region.IsRegion, $"{key}'s search region is not a list of places");
                Assert.False(region.IsPlainText, $"{key}'s region went back to a plain box");

                // One row is one place to look, and a place is either four numbers or a variable
                // that says where to look — a picture taken earlier, most often.
                Assert.False(region.HasRegions, $"{key} starts with somewhere to look");
                region.AddRegion();
                Assert.Single(region.Regions);
                Assert.True(region.HasRegions);
                Assert.Empty(region.Regions[0].ToRow().Columns);

                // What the region picker dragged out lands in the row that asked for it.
                region.Regions[0].Put(10, 20, 30, 40);
                Assert.Equal("10", region.Regions[0].ToRow().Text("x"));
                Assert.Equal("20", region.Regions[0].ToRow().Text("y"));
                Assert.Equal("30", region.Regions[0].ToRow().Text("width"));
                Assert.Equal("40", region.Regions[0].ToRow().Text("height"));

                region.AddRegion(new RegionRowViewModel { OrVariable = "$shot" });
                Assert.Equal(2, region.Regions.Count);
                Assert.Equal("$shot", region.Regions[1].ToRow().Text("text"));

                region.RemoveRegion(region.Regions[0]);
                Assert.Single(region.Regions);
                Assert.Equal("$shot", region.Regions[0].ToRow().Text("text"));
            }
        });
    }

    /// <summary>
    /// A window is one captured rule rather than a text box beside a comparison choice. The field
    /// can still be handed a variable holding a window record, through its own variable picker.
    /// </summary>
    [Fact]
    public void A_window_is_one_rule_and_a_variable_can_be_used_instead()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "window.exists", "vision.findImage", "input.keyPress" })
            {
                var field = Parameter(Open(key), "window");

                Assert.True(field.IsWindowRule, $"{key}'s window is not one rule field");
                Assert.False(field.OffersFormula, $"{key}'s window offers the expression editor");

                field.UseWindowVariable("count");
                Assert.Equal("$count", field.Text);
            }
        });
    }

    /// <summary>
    /// Every action that names a window uses the same one field shape.
    /// </summary>
    [Fact]
    public void Every_window_is_one_rule_field()
    {
        var wrong = Ui.Run(() =>
        {
            return ActionCatalog.Definitions
                .SelectMany(definition => definition.Parameters)
                .Where(parameter => parameter.Name == "window"
                    && parameter.Kind is not ActionParameterKind.WindowRule)
                .Select(parameter => $"{parameter.OwnerKey}.{parameter.Name}")
                .Order()
                .ToList();
        });

        Assert.Empty(wrong);
    }

    /// <summary>Working a value out is the value half of Set Variable, not a second action.</summary>
    [Fact]
    public void Working_a_value_out_is_part_of_set_variable()
    {
        var set = Ui.Run(() => ActionCatalog.RunnableActions
            .FirstOrDefault(action => action.Key == "control.setVariable"));

        Assert.NotNull(set);
        Assert.Equal(ActionCategory.Control, set.Category);
        Assert.Equal("设置变量", ActionStrings.Chinese["control.setVariable.name"]);
        Assert.DoesNotContain(ActionCatalog.RunnableActions, action =>
            action.Key is "control.calculate" or "control.delayRandom" or "control.listCreate"
                or "control.sequence");
    }

    /// <summary>
    /// A field that takes a variable by name is a box of text, so what it is offered has to be the
    /// names: the rows the wall lists would be spelled out as the name of their own type, and one
    /// picked from the dropdown would put that into the macro rather than the variable it stands
    /// for. What comes with the name follows what the field does with it: a name being written goes
    /// in bare — the engine writes that text down as the name, so <c>$count</c> there would make a
    /// variable called <c>$count</c> that nothing can read back — and a value being read goes in as
    /// the reader writes it, with the dollar sign the wall hands back.
    /// </summary>
    [Fact]
    public void A_field_that_takes_a_variable_by_name_offers_the_names_and_takes_them_bare()
    {
        Ui.Run(() =>
        {
            // Naming a variable to write: set variable's own name.
            var naming = Parameter(Open("control.setVariable"), "name");
            Assert.True(naming.IsVariable);
            Assert.Equal(["count", "name"], naming.NameChoices.Order(StringComparer.Ordinal));

            naming.InsertVariable("$count", 0);
            Assert.Equal("count", naming.Text);

            var reading = Parameter(Open("control.if"), "condition");
            reading.ApplyVariableDrop("$count");
            Assert.Equal("$count", reading.Text);
            Assert.Contains("$count", reading.ExpressionSuggestions);
        });
    }

    /// <summary>
    /// A box that writes a name is offered only the names the macro may write. A box that reads a
    /// value is offered every scope, including what WhaleGenie provides and what an earlier step
    /// left behind, because both are values a macro can read even though it cannot write them.
    /// </summary>
    [Fact]
    public void A_box_that_holds_a_name_is_offered_the_names_the_macro_works_with()
    {
        Ui.Run(() =>
        {
            var variables = VariableChoices.Merge(
                VariableChoicesForChecks.Named("count"),
                [
                    new VariableChoice { Name = "notes", Scope = VariableScope.Global },
                    new VariableChoice { Name = "sys.date", Scope = VariableScope.System },
                    new VariableChoice { Name = "sys.mouseX", Scope = VariableScope.System },
                    new VariableChoice
                    {
                        Name = "step.k3f9.outcome",
                        Scope = VariableScope.Local,
                        IsStepResult = true,
                    },
                ]);

            var viewModel = new AddActionViewModel(ActionCatalog.Definitions, variables, []);

            // A name being written: the names themselves.
            viewModel.SelectAction("control.setVariable");
            Assert.Equal(["count", "notes"], Parameter(viewModel, "name").NameChoices);

            // A value being read: every scope, in the form the reader writes a variable in. A path
            // is one of these, and neither a provided value nor a step's ending is something to
            // spell from memory.
            viewModel.SelectAction("file.writeText");
            Assert.Equal(
                ["$count", "$notes", "$step.k3f9.outcome", "$sys.date", "$sys.mouseX"],
                Parameter(viewModel, "path").NameChoices.Order(StringComparer.Ordinal));
        });
    }

    /// <summary>
    /// A provided value and a step's ending are both readable, so a value box lists them with the
    /// dollar sign the reader uses; a box that writes a name lists neither, because neither may be
    /// written by a macro.
    /// </summary>
    [Fact]
    public void A_value_box_lists_every_scope_but_a_written_name_does_not()
    {
        Ui.Run(() =>
        {
            var variables = VariableChoices.Merge(
                VariableChoicesForChecks.Named("count"),
                [
                    new VariableChoice { Name = "sys.macroFolder", Scope = VariableScope.System },
                    new VariableChoice
                    {
                        Name = "step.k3f9.error",
                        Scope = VariableScope.Local,
                        IsStepResult = true,
                    },
                ]);

            var viewModel = new AddActionViewModel(ActionCatalog.Definitions, variables, []);

            viewModel.SelectAction("file.writeText");
            var path = Parameter(viewModel, "path");
            Assert.Contains("$sys.macroFolder", path.NameChoices);
            Assert.Contains("$step.k3f9.error", path.NameChoices);
            Assert.Contains("$sys.macroFolder", path.ExpressionSuggestions);
            Assert.Contains("$step.k3f9.error", path.ExpressionSuggestions);

            viewModel.SelectAction("control.setVariable");
            var name = Parameter(viewModel, "name");
            Assert.Equal(["count"], name.NameChoices);
        });
    }

    /// <summary>
    /// The picker opens the expression editor for the fields whose whole value is read at once, and
    /// only for those. A box that writes a variable's name is left out — a formula there would become
    /// the name itself, and nothing would ever read it back — and so is the box that recognises a
    /// window by text, which is filled in with names rather than worked out.
    /// </summary>
    [Fact]
    public void The_picker_offers_the_expression_editor_where_a_whole_value_is_read()
    {
        Ui.Run(() =>
        {
            // Read as a whole, so a value worked out from variables and functions belongs here.
            Assert.True(Parameter(Open("file.writeText"), "path").OffersBuilder);
            Assert.True(Parameter(Open("system.notify"), "notificationText").OffersBuilder);

            // A name being written, and a window recognised by text: both take the picker, neither
            // takes a formula.
            Assert.False(Parameter(Open("control.setVariable"), "name").OffersBuilder);
            Assert.False(Parameter(Open("control.for"), "name").OffersBuilder);
            Assert.False(Parameter(Open("window.exists"), "window").OffersBuilder);
        });
    }

    /// <summary>
    /// A picture is a file or the picture an earlier Capture saved, and the second of those is a
    /// name: it goes over the box the same way it goes over any other box that takes one, on the
    /// one picture a step names and on every row of a list of them.
    /// </summary>
    [Fact]
    public void A_picture_box_offers_the_pictures_an_earlier_step_saved()
    {
        Ui.Run(() =>
        {
            var viewModel = new AddActionViewModel(ActionCatalog.Definitions,
                VariableChoicesForChecks.Named("count", "shot"), []);

            viewModel.SelectAction("clipboard.writeImage");
            var picture = Parameter(viewModel, "imageSource");
            Assert.True(picture.IsImage);
            Assert.Equal(["$count", "$shot"], picture.NameChoices);

            // Every row of a list has to say it, not only the one the dialog happens to show first.
            viewModel.SelectAction("vision.findImage");
            var list = Parameter(viewModel, "images");
            list.AddPicture();
            list.AddPicture();
            Assert.Equal(2, list.Pictures.Count);
            Assert.All(list.Pictures,
                row => Assert.Equal(["$count", "$shot"], row.Suggestions));
        });
    }

    /// <summary>
    /// Working a value out is one expression, now that the expression editor exists: the operands
    /// and the operator they were picked from are gone, and the whole value is offered the builder
    /// the same way storing a value is.
    /// </summary>
    [Fact]
    public void A_calculation_is_one_expression_that_can_be_built_with_the_editor()
    {
        Ui.Run(() =>
        {
            var calculate = Open("control.setVariable");

            var value = Parameter(calculate, "value");

            Assert.True(value.IsExpression);
            Assert.NotEmpty(value.ExpressionSuggestions);

            // The pieces the expression replaced are gone, not just hidden.
            Assert.DoesNotContain(calculate.Parameters,
                parameter => parameter.Definition.Name is "left" or "right" or "operator");
        });
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
            Path.Combine(Repository(), "WhaleGenie", "Views", "ActionEditorView.axaml"));

        var start = markup.IndexOf("IsVisible=\"{Binding OffersVariables}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the dialog stopped offering variables in a field that takes them");

        var block = markup[start..markup.IndexOf("</Grid>", start, StringComparison.Ordinal)];
        Assert.Contains("AutoCompleteBox", block, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding ExpressionSuggestions}\"", block, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding OffersFormula}\"", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnOpenExpressionBuilder\"", block, StringComparison.Ordinal);

        // And a field that holds a variable by name offers the names: the box holds text, so the
        // rows of the wall listed there would be written into the macro as the name of their type.
        var named = markup.IndexOf("IsVisible=\"{Binding IsVariable}\"", StringComparison.Ordinal);
        Assert.True(named >= 0, "the dialog stopped offering variables in the field that names one");

        var naming = markup[named..markup.IndexOf("</Grid>", named, StringComparison.Ordinal)];
        Assert.Contains("ItemsSource=\"{Binding NameChoices}\"", naming, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPickVariable\"", naming, StringComparison.Ordinal);

        // And a picture, which is a file or the picture an earlier step saved under a name, is
        // offered those names as well.
        var pictured = markup.IndexOf("IsVisible=\"{Binding IsImage}\"", StringComparison.Ordinal);
        Assert.True(pictured >= 0, "the dialog stopped editing a picture in a box of its own");

        var pictures = markup[pictured..markup.IndexOf("</StackPanel>", pictured,
            StringComparison.Ordinal)];
        Assert.Contains("ItemsSource=\"{Binding NameChoices}\"", pictures, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every field whose hint names variables has to be a box that offers them. The ones left out
    /// are named here with the reason, so leaving another one out by accident fails this check.
    /// </summary>
    [Fact]
    public void No_field_promising_variables_is_left_without_them()
    {
        // A name of something else: a variable of the machine's own environment, the names a step
        // writes into a file rather than reads, and the placeholders inside a script, which belong
        // to the other language: a value gets into a script the way that language says, or by a
        // placeholder written into the script itself, rather than by a list of names standing over
        // the box it was written in. A page's own script is the same kind of box.
        string[] otherNames = ["file.saveVariables.names", "control.fail.failureKey",
            "system.environment.environmentName",
            "script.run.script", "script.ahk.script", "browser.evaluate.script"];

        // A box of several lines: a list of names standing over one would be in the way, so these
        // are written by hand. The first fills a `$name` in, and the second is shown exactly as it
        // was written.
        string[] multiline = ["command.run.standardInput", "control.note.note"];

        // The shape a reading has to fit is a pattern rather than a value: the dollar signs its hint
        // uses are the ends of a regular expression, not the start of a variable name.
        string[] aPattern = ["ocr.findText.expected", "ocr.clickText.expected"];

        // Only the kinds that are a bare box are looked at, because only those can be a box a name
        // is typed into and nothing offers it: a region says a variable in a cell filled from the
        // wall, a choice picks one of its own values, a whole value is built in the expression
        // editor and a list of results is named on its own page, so what their hints say about
        // variables is prose rather than a promise about a box.
        ActionParameterKind[] boxes =
        [
            ActionParameterKind.Text, ActionParameterKind.MultilineText,
            ActionParameterKind.Image, ActionParameterKind.Images,
        ];

        var gaps = Ui.Run(() => ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters.Select(parameter => (definition.Key, parameter)))
            .Where(row => boxes.Contains(row.parameter.Kind))
            .Where(row => row.parameter.Hint.Contains("variable", StringComparison.OrdinalIgnoreCase)
                || row.parameter.Hint.Contains('$'))
            .Where(row => !row.parameter.OffersNames)
            .Select(row => $"{row.Key}.{row.parameter.Name}")
            .Where(key => !otherNames.Contains(key) && !multiline.Contains(key)
                && !aPattern.Contains(key))
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
        var viewModel = new AddActionViewModel(ActionCatalog.Definitions,
            VariableChoicesForChecks.Named("count", "name"), []);
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
