using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// Taking a UI Automation element off the screen, so a macro can find it again by a selector
/// instead of by a place on the screen. What the picker writes has to read back as exactly what
/// the engine will go looking for, and the outline must leave the control underneath reachable.
/// </summary>
public class ElementPickerTests
{
    [Fact]
    public void An_element_is_written_as_the_stablest_thing_it_offers()
    {
        var button = new UiElementInfo("Save", "saveButton", "Button", "ButtonClass",
            new ScreenPoint(10, 20), new ScreenSize(80, 24), "Untitled - Notepad");

        // An automation id does not follow the interface language, so it is preferred.
        Assert.Equal("Button[automationId='saveButton']", button.Selector);
        Assert.Equal("Untitled - Notepad", button.WindowTitle);
    }

    [Fact]
    public void A_control_without_an_id_falls_back_to_the_name_a_person_reads()
    {
        var button = new UiElementInfo("Save", string.Empty, "Button", string.Empty,
            new ScreenPoint(0, 0), new ScreenSize(10, 10), string.Empty);

        Assert.Equal("Button[name='Save']", button.Selector);
    }

    [Fact]
    public void A_control_with_nothing_to_identify_it_keeps_only_its_kind()
    {
        var pane = new UiElementInfo(string.Empty, string.Empty, "Pane", string.Empty,
            new ScreenPoint(0, 0), new ScreenSize(10, 10), string.Empty);

        Assert.Equal("Pane", pane.Selector);
    }

    [Fact]
    public void A_value_the_reader_would_change_is_left_out_of_the_selector()
    {
        // The reader splits a selector on commas and strips the quotes around a value, so a name
        // holding either would come back as a different string and quietly match the wrong thing.
        var comma = new UiElementInfo("Save, and close", string.Empty, "Button", string.Empty,
            new ScreenPoint(0, 0), new ScreenSize(10, 10), string.Empty);

        Assert.Equal("Button", comma.Selector);

        var quoted = new UiElementInfo("Say \"hi\"", "saveButton", "Button", string.Empty,
            new ScreenPoint(0, 0), new ScreenSize(10, 10), string.Empty);

        // The id is still worth writing on its own.
        Assert.Equal("Button[automationId='saveButton']", quoted.Selector);
    }

    [Fact]
    public void A_query_is_written_in_the_order_the_engine_reads_it()
    {
        var query = new UiQuery(
            Name: "Save",
            AutomationId: "save",
            ControlType: "Button",
            ClassName: "ButtonClass");

        Assert.Equal("Button[automationId='save', name='Save', className='ButtonClass']",
            query.ToSelector());
    }

    [Fact]
    public void The_picker_takes_the_control_the_pointer_was_put_on()
    {
        Ui.Run(() =>
        {
            var window = new ElementPickerWindow((x, y) => At(x, y));

            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ProbeAt(300, 200);

            var hovered = Assert.IsType<UiElementInfo>(window.Hovered);
            Assert.Equal("Save", hovered.Name);
            Assert.Equal("Untitled - Notepad", hovered.WindowTitle);
            Assert.Null(window.Chosen);

            window.Confirm();

            Assert.Equal(
                new ElementPickerWindow.Pick("Button[automationId='saveButton']", "Untitled - Notepad"),
                window.Chosen);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void Coming_back_onto_a_control_does_not_open_the_picker_a_second_time()
    {
        Ui.Run(() =>
        {
            var found = false;
            var window = new ElementPickerWindow((x, y) => found ? At(x, y) : null);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // The pointer crosses empty desktop and comes back onto a control, which takes the
            // frame off the screen and puts it back. Putting it back opens the window again, and
            // that must not build a second readout: the words already belong to the first one, and
            // a control with two parents throws instead of drawing.
            window.ProbeAt(300, 200);
            Assert.False(window.IsVisible);

            found = true;
            window.ProbeAt(300, 200);

            Assert.True(window.IsVisible);
            Assert.NotNull(window.Hovered);
            window.Close();
        });
    }

    [Fact]
    public void A_place_with_nothing_on_it_is_not_taken()
    {
        Ui.Run(() =>
        {
            var found = false;
            var window = new ElementPickerWindow((x, y) => found ? At(x, y) : null);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ProbeAt(300, 200);
            Assert.Null(window.Hovered);

            // Nothing worth writing, so the picker takes nothing.
            window.Confirm();
            Assert.Null(window.Chosen);

            // And it is still picking: the next place the pointer lands is taken as usual, which
            // is what "the picker stays up" means — its frame is off the screen only because
            // there is nothing under the pointer to outline.
            found = true;
            window.ProbeAt(300, 200);
            window.Confirm();

            Assert.Equal("Button[automationId='saveButton']", window.Chosen!.Selector);
        });
    }

    [Fact]
    public void Cancelling_leaves_without_a_control()
    {
        Ui.Run(() =>
        {
            var window = new ElementPickerWindow((x, y) => At(x, y));

            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ProbeAt(300, 200);
            window.Cancel();

            Assert.Null(window.Chosen);
            Assert.False(window.IsVisible);
        });
    }

    [Fact]
    public void The_frame_is_the_border_and_never_the_hole()
    {
        // A box at 100,100 that is 200 wide and 100 tall, outlined 3 pixels thick.
        Assert.True(ElementPickerWindow.OnFrame(100, 100, 100, 100, 200, 100, 3));
        Assert.True(ElementPickerWindow.OnFrame(299, 199, 100, 100, 200, 100, 3));
        Assert.True(ElementPickerWindow.OnFrame(150, 100, 100, 100, 200, 100, 3));

        // The middle is the hole the control underneath goes on seeing the pointer through.
        Assert.False(ElementPickerWindow.OnFrame(150, 150, 100, 100, 200, 100, 3));

        // Anything outside the outline belongs to somebody else.
        Assert.False(ElementPickerWindow.OnFrame(99, 150, 100, 100, 200, 100, 3));
        Assert.False(ElementPickerWindow.OnFrame(150, 200, 100, 100, 200, 100, 3));

        // The smallest box still keeps a hole in the middle.
        Assert.True(ElementPickerWindow.OnFrame(100, 100, 100, 100, 7, 7, 3));
        Assert.False(ElementPickerWindow.OnFrame(103, 103, 100, 100, 7, 7, 3));

        // A box with no room left for a hole cannot be an outline at all.
        Assert.False(ElementPickerWindow.OnFrame(100, 100, 100, 100, 6, 6, 3));
    }

    [Fact]
    public void The_frame_is_answered_from_where_it_was_drawn()
    {
        Ui.Run(() =>
        {
            var found = true;
            var window = new ElementPickerWindow((x, y) => found ? At(x, y) : null);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ProbeAt(300, 200);

            // The control sits at 300,200 and the frame is drawn three pixels outside it, so the
            // border is the picker's own and the hole in the middle belongs to the control.
            Assert.True(FromTheHookThread(window, 320, 198));
            Assert.False(FromTheHookThread(window, 340, 210));

            // With nothing under the pointer the frame comes off the screen, and a click anywhere
            // is then somebody else's business.
            found = false;
            window.ProbeAt(300, 200);
            Assert.False(FromTheHookThread(window, 320, 198));

            window.Close();
        });
    }

    /// <summary>
    /// Asks the picker from a thread that owns nothing, the way the mouse hook does. A checked
    /// build is where this bites: Avalonia then refuses to have a window read from a thread that
    /// did not make it, and an exception out of a hook has nowhere to go but out of the native
    /// callback, which ends the process instead of raising anything a caller could catch.
    /// </summary>
    private static bool FromTheHookThread(ElementPickerWindow window, int x, int y)
        => Task.Run(() => window.Handles(new ScreenPoint(x, y))).GetAwaiter().GetResult();

    [Fact]
    public void Every_action_that_looks_for_an_element_offers_a_selector()
    {
        var keys = Ui.Run(() => ActionCatalog.Definitions
            .Where(definition => definition.Parameters.Any(parameter => parameter.Name == "uiSelector"))
            .Select(definition => definition.Key)
            .Order()
            .ToList());

        Assert.Equal(
            ["condition.uiaExists", "condition.uiaNotExists", "uia.check", "uia.click", "uia.exists",
             "uia.expand", "uia.find", "uia.invoke", "uia.read", "uia.readTable", "uia.scrollIntoView",
             "uia.select", "uia.setText", "uia.waitElement"],
            keys);
    }

    [Fact]
    public void A_selector_is_edited_with_the_element_picker()
    {
        Ui.Run(() =>
        {
            var definition = ActionCatalog.Find("uia.setText")
                ?? throw new InvalidOperationException("uia.setText is missing from the catalogue.");

            var selector = new StepParameterViewModel(
                definition.Parameters.First(parameter => parameter.Name == "uiSelector"));
            var text = new StepParameterViewModel(
                definition.Parameters.First(parameter => parameter.Name == "valueText"));

            Assert.True(selector.IsText);
            Assert.True(selector.IsSelector);

            // The bare one-line field belongs to every other text parameter, so the selector is
            // never drawn twice on top of itself.
            Assert.False(selector.IsPlainText);
            Assert.False(text.IsSelector);

            // What the step writes is read as one value, so that field is the one with the
            // variables over it and the expression editor beside it.
            Assert.True(text.OffersVariables);
            Assert.True(text.OffersFormula);
        });
    }

    /// <summary>
    /// An element of a page is picked off the page, not off the desktop, so it carries its own
    /// button. The bare one-line field would otherwise be drawn underneath it as well.
    /// </summary>
    [Fact]
    public void An_element_of_a_page_is_picked_off_the_page()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "browser.click", "browser.fill", "browser.readText" })
            {
                var definition = ActionCatalog.Find(key)
                    ?? throw new InvalidOperationException($"{key} is missing from the catalogue.");
                var target = new StepParameterViewModel(
                    definition.Parameters.First(parameter => parameter.Name == "pageSelector"));

                Assert.True(target.IsBrowserTarget, $"{key}'s element is not picked off the page");
                Assert.False(target.IsPlainText, $"{key}'s element is drawn twice");
                Assert.False(target.OffersVariables, $"{key}'s element is drawn twice");
                Assert.False(target.IsSelector, $"{key}'s element got the UI Automation picker");
            }

            // A page is opened by the browser's own address field: it names no element of the page,
            // so it takes no page picker — but it is a value the step reads, so the variables and
            // the expression editor are over it like anywhere else.
            var open = ActionCatalog.Find("browser.open")
                ?? throw new InvalidOperationException("browser.open is missing from the catalogue.");
            var url = new StepParameterViewModel(
                open.Parameters.First(parameter => parameter.Name == "url"));
            Assert.False(url.IsBrowserTarget);
            Assert.False(url.IsSelector);
            Assert.True(url.OffersFormula);
        });
    }

    /// <summary>
    /// The flag only means something if the dialog's markup uses it, so the block that draws the
    /// page picker is read here: a field for it, and a button that asks for a pick.
    /// </summary>
    [Fact]
    public void The_dialog_puts_the_page_picker_beside_an_element_of_a_page()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AddActionWindow.axaml"));

        var start = markup.IndexOf("IsVisible=\"{Binding IsBrowserTarget}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the dialog stopped offering a picker for elements of a page");

        var block = markup[start..markup.IndexOf("</Grid>", start, StringComparison.Ordinal)];
        Assert.Contains("TextBox", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPickBrowserElement\"", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// A field naming an element of a page is several lines tall, because a page that has been
    /// redesigned is met by writing a second way of naming the element under the first, and the
    /// ways a pick found are offered under the box for exactly that.
    /// </summary>
    [Fact]
    public void The_dialog_takes_a_second_way_of_naming_an_element_of_a_page()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AddActionWindow.axaml"));

        var start = markup.IndexOf("IsVisible=\"{Binding IsBrowserTarget}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the dialog stopped offering a picker for elements of a page");

        var end = markup.IndexOf("IsVisible=\"{Binding IsMultiline}\"", start, StringComparison.Ordinal);
        Assert.True(end > start, "the dialog stopped drawing the page picker in a block of its own");

        var block = markup[start..end];
        Assert.Contains("AcceptsReturn=\"True\"", block, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Candidates}\"", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAddBrowserWay\"", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPickBrowserVariable\"", block, StringComparison.Ordinal);
    }

    /// <summary>
    /// What a pick leaves beside the field: the other ways of naming the element it found, each
    /// saying what kind of naming it is and how many elements it names, and each adding a line when
    /// it is taken. A name picked from the variable wall goes on a line of its own for the same
    /// reason — the lines are tried one after another.
    /// </summary>
    [Fact]
    public void The_ways_of_naming_the_element_a_pick_found_are_offered_beside_the_field()
    {
        Ui.Run(() =>
        {
            var definition = ActionCatalog.Find("browser.click")
                ?? throw new InvalidOperationException("browser.click is missing from the catalogue.");
            var target = new StepParameterViewModel(
                definition.Parameters.First(parameter => parameter.Name == "pageSelector"));

            // A name written in the box is filled in when the step runs, so the names are offered
            // beside the picker — the box itself carries no list over it — and a formula is not
            // offered at all, because it would be written down as part of the selector.
            Assert.True(target.OffersPageVariables);
            Assert.False(target.OffersBuilder);
            Assert.False(target.HasCandidates);

            target.Text = "#save";
            target.OfferCandidates(
            [
                new PickCandidate(PickKind.DataAttribute, "[data-testid=\"save\"]", 1),
                new PickCandidate(PickKind.Path, "body > div > button", 3),
            ]);

            Assert.True(target.HasCandidates);
            Assert.Equal(2, target.Candidates.Count);
            Assert.Contains("names 1", target.Candidates[0].Text, StringComparison.Ordinal);
            Assert.Contains("names 3", target.Candidates[1].Text, StringComparison.Ordinal);

            // Taking one writes it under what the pick wrote first, which is the order the step will
            // try them in.
            var way = target.Candidates[1];
            way.Take!(way);
            Assert.Equal("#save" + Environment.NewLine + "body > div > button", target.Text);

            target.AddLine("$choice");
            Assert.Equal(
                "#save" + Environment.NewLine + "body > div > button" + Environment.NewLine + "$choice",
                target.Text);
        });
    }

    /// <summary>
    /// What a page step adds when a selector names five elements that look alike. It is a list
    /// rather than one more box beside the selector, because more than one thing can have to hold
    /// at once, and the rows are what the engine is handed as the conditions of the target.
    /// </summary>
    [Fact]
    public void A_page_step_can_say_what_else_the_element_has_to_be_like()
    {
        Ui.Run(() =>
        {
            var narrow = Narrow("browser.click");

            // Nothing filled in is what every step did before this field existed, so an untouched
            // field holds no rows and is left out of the step altogether.
            Assert.False(narrow.IsIncluded);
            Assert.Empty(narrow.Conditions());

            narrow.AddNarrowRow();
            var row = Assert.Single(narrow.NarrowRows);
            row.Way = Way(row, PageFilter.HasText);
            row.Value = "确定";

            // A row is handed to the engine the way it was written, and it is what says the element
            // has to be like that as well as being found by the selector.
            Assert.Equal([new PageCondition(PageFilter.HasText, "确定")], narrow.Conditions());
            Assert.True(narrow.IsIncluded);

            // What the step writes down is the way of narrowing and what it looks for. A row that
            // was added and left empty is not a condition and is not written down either.
            narrow.AddNarrowRow();
            var written = Assert.Single(narrow.ToStepParameter().Rows);
            Assert.Equal("HasText", written.Text("how"));
            Assert.Equal("确定", written.Text("value"));

            // And the same rows are in front of the user when the step is opened again.
            var reopened = Narrow("browser.click");
            reopened.ApplyValue(narrow.ToStepParameter());
            var back = Assert.Single(reopened.NarrowRows);
            Assert.Equal("HasText", back.Way.Value);
            Assert.Equal("确定", back.Value);
        });
    }

    /// <summary>
    /// The one way of narrowing that asks nothing beside it: "only what can be seen" is about the
    /// element itself, so its row carries no value box and is written down as the way alone. A row
    /// that asks about the writing is the one a pick off the page fills in with words.
    /// </summary>
    [Fact]
    public void Only_what_can_be_seen_asks_nothing_beside_it()
    {
        Ui.Run(() =>
        {
            var narrow = Narrow("browser.upload");
            narrow.AddNarrowRow();
            var row = Assert.Single(narrow.NarrowRows);
            Assert.True(row.NeedsValue);
            Assert.False(row.WantsWords);

            row.Way = Way(row, PageFilter.Visible);
            Assert.False(row.NeedsValue);
            Assert.False(row.WantsWords);
            Assert.Equal([new PageCondition(PageFilter.Visible, string.Empty)], narrow.Conditions());

            var written = Assert.Single(narrow.ToStepParameter().Rows);
            Assert.Equal("Visible", written.Text("how"));
            Assert.Equal(string.Empty, written.Text("value"));

            row.Way = Way(row, PageFilter.HasNotText);
            Assert.True(row.WantsWords);
        });
    }

    /// <summary>
    /// A row is looked at on top of the rows above it, which is what makes "did adding this one
    /// help" a question the dialog can answer: the count beside a row is what the page holds with
    /// that row and the ones before it, and the rows below change nothing about it.
    /// </summary>
    [Fact]
    public void A_row_is_looked_at_with_the_rows_above_it()
    {
        Ui.Run(() =>
        {
            var narrow = Narrow("browser.click");
            narrow.AddNarrowRow();
            narrow.AddNarrowRow();
            narrow.AddNarrowRow();
            var rows = narrow.NarrowRows.ToList();
            rows[0].Way = Way(rows[0], PageFilter.HasText);
            rows[0].Value = "第一";
            rows[1].Way = Way(rows[1], PageFilter.Visible);
            rows[2].Way = Way(rows[2], PageFilter.HasText);
            rows[2].Value = "第三";

            Assert.Equal([new PageCondition(PageFilter.HasText, "第一")], narrow.Conditions(rows[0]));
            Assert.Equal(2, narrow.Conditions(rows[1]).Count);
            Assert.Equal(3, narrow.Conditions(rows[2]).Count);
            Assert.Equal(3, narrow.Conditions().Count);
        });
    }

    /// <summary>
    /// The dialog reads the list the flag says it draws: a dropdown for the way of narrowing, a box
    /// for what to look for by it, a button that picks it off the page and one that looks at what
    /// the page holds with the rows above, and a button that takes the row out again.
    /// </summary>
    [Fact]
    public void The_dialog_draws_a_page_step_s_conditions_one_row_each()
    {
        var markup = File.ReadAllText(
            Path.Combine(Repository(), "WhaleGenie", "Views", "AddActionWindow.axaml"));

        var start = markup.IndexOf("IsVisible=\"{Binding IsNarrow}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the dialog stopped drawing the conditions of a page element");

        var end = markup.IndexOf("IsVisible=\"{Binding IsBool}\"", start, StringComparison.Ordinal);
        Assert.True(end > start, "the dialog stopped drawing the conditions in a block of its own");

        var block = markup[start..end];
        Assert.Contains("ItemsSource=\"{Binding NarrowRows}\"", block, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding Choices}\"", block, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Value}\"", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnAddNarrowRow\"", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnPickNarrowValue\"", block, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnLookNarrowRow\"", block, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding RemoveCommand}\"", block, StringComparison.Ordinal);
    }

    /// <summary>The field a page step says what its element has to be like in.</summary>
    private static StepParameterViewModel Narrow(string key)
        => new((ActionCatalog.Find(key)
                ?? throw new InvalidOperationException($"{key} is missing from the catalogue."))
            .Parameters.First(parameter => parameter.Name == "narrow"));

    /// <summary>One way of narrowing, as the row offers it.</summary>
    private static ActionParameterOption Way(NarrowRowViewModel row, PageFilter way)
        => row.Choices.First(choice => choice.Value == way.ToString());

    /// <summary>The control a test pretends the pointer was put on.</summary>
    private static UiElementInfo At(int x, int y) => new(
        "Save",
        "saveButton",
        "Button",
        "ButtonClass",
        new ScreenPoint(x, y),
        new ScreenSize(80, 24),
        "Untitled - Notepad");

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
