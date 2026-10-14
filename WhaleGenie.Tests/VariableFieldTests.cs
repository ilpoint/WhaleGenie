using WhaleGenie.Core.Devices;
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
            Assert.True(message.OffersFormula);
            Assert.False(message.IsPlainText);
            Assert.NotEmpty(message.ExpressionSuggestions);
            Assert.Contains(message.ExpressionSuggestions, entry => entry == "$count");

            Assert.True(heading.OffersVariables);
            Assert.True(heading.OffersFormula);
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
                         "ocr.findText", "condition.imageExists", "condition.imageNotExists",
                         "condition.textExists", "condition.textNotExists",
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
    /// The text a window is recognised by is written like any other text a macro works out earlier:
    /// a name goes in as it is typed and the window picker stands beside the same box. The whole
    /// value is not read as one, so the expression editor stays away from it: a window name is a
    /// name rather than a calculation, and $name in it is filled in where it stands.
    /// </summary>
    [Fact]
    public void A_window_is_named_in_the_box_the_variables_are_offered_in()
    {
        Ui.Run(() =>
        {
            foreach (var (key, name) in new[]
                     {
                         ("window.exists", "window"),
                         ("vision.findImage", "anchorWindow"),
                         ("input.keyPress", "targetWindow"),
                     })
            {
                var field = Parameter(Open(key), name);

                Assert.True(field.IsWindow, $"{key}'s {name} is not a window field");
                Assert.True(field.OffersVariables, $"{key}'s {name} offers no variables");
                Assert.False(field.OffersFormula, $"{key}'s {name} offers the expression editor");
                Assert.False(field.IsPlainText, $"{key}'s {name} went back to a plain box");
                Assert.Contains(field.ExpressionSuggestions, entry => entry == "$count");
            }
        });
    }

    /// <summary>
    /// How a window is recognised is one choice of five, the same five wherever a window is named:
    /// a window action, a step that reads the screen and one that posts its input at a window all
    /// offer the values the engine reads, in the order it reads them.
    /// </summary>
    [Fact]
    public void Every_window_rule_offers_the_same_five_ways()
    {
        var wrong = Ui.Run(() =>
        {
            var found = new List<string>();
            foreach (var definition in ActionCatalog.Definitions)
            {
                // "How" is also the word a browser step uses for which tab it switches to, which is
                // a different question: a window rule is found by the window field beside it.
                if (!definition.Parameters.Any(parameter =>
                        parameter.Kind is ActionParameterKind.Window))
                {
                    continue;
                }

                foreach (var way in definition.Parameters.Where(parameter =>
                             parameter.Name is "how" or "anchorHow" or "targetHow"))
                {
                    if (!way.Options.SequenceEqual(WindowHow.Values))
                    {
                        found.Add($"{definition.Key}.{way.Name}");
                    }
                }
            }

            return found;
        });

        Assert.Empty(wrong);
    }

    /// <summary>
    /// Working a value out is an action, not only a field: a macro that has numbers or text to
    /// put together picks it from the step list like any other step.
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

            // Reading one: a value compared against a number takes the reader's own form, which is
            // what the wall hands back.
            var reading = Parameter(Open("condition.valueInRange"), "value");
            reading.InsertVariable("$count", 0);
            Assert.Equal("$count", reading.Text);
            Assert.Equal(["$count", "$name"], reading.NameChoices.Order(StringComparer.Ordinal));
        });
    }

    /// <summary>
    /// A box that holds a name is offered the names the macro works with rather than everything the
    /// wall can show. What WhaleGenie provides is read-only — the dialog refuses the name the
    /// moment it is written into a step — and what a step leaves behind is written by the engine
    /// rather than named by anybody, so neither is a name to pick out of a list. Both are read off
    /// the wall, which shows every scope.
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

            // A value being read out of a box that holds a name: the same names, in the form the
            // reader writes a variable in. A path is one of these, and a name is not something to
            // spell from memory.
            viewModel.SelectAction("vision.saveImage");
            Assert.Equal(["$count", "$notes"], Parameter(viewModel, "path").NameChoices);
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
            Assert.True(Parameter(Open("vision.saveImage"), "path").OffersBuilder);
            Assert.True(Parameter(Open("system.notify"), "message").OffersBuilder);

            // A name being written, and a window recognised by text: both take the picker, neither
            // takes a formula.
            Assert.False(Parameter(Open("control.setVariable"), "name").OffersBuilder);
            Assert.False(Parameter(Open("control.for"), "variable").OffersBuilder);
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

            viewModel.SelectAction("vision.saveImage");
            var picture = Parameter(viewModel, "image");
            Assert.True(picture.IsImage);
            Assert.Equal(["$count", "$shot"], picture.NameChoices);

            // Every row of a list has to say it, not only the one the dialog happens to show first.
            viewModel.SelectAction("vision.findImage");
            var list = Parameter(viewModel, "image");
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
            var calculate = Open("control.calculate");

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
            Path.Combine(Repository(), "WhaleGenie", "Views", "AddActionWindow.axaml"));

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
        // to the other language.
        string[] otherNames = ["file.saveVariables.names", "system.environment.name",
            "script.run.script"];

        // A box of several lines: a list of names standing over one would be in the way, so these
        // are written by hand. The first fills a `$name` in, and the second is shown exactly as it
        // was written.
        string[] multiline = ["command.run.standardInput", "control.note.text"];

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

        // A window is written in the box the variables are offered in, so every field that names one
        // says it takes them: that answer is what puts the list of names over the box.
        var silent = Ui.Run(() => ActionCatalog.Definitions
            .SelectMany(definition => definition.Parameters.Select(parameter => (definition.Key, parameter)))
            .Where(row => row.parameter.Kind is ActionParameterKind.Window)
            .Where(row => !row.parameter.AcceptsVariables)
            .Select(row => $"{row.Key}.{row.parameter.Name}")
            .Order()
            .ToList());

        Assert.Empty(silent);
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
