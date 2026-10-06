using Avalonia.Threading;
using Viktor.Core.Devices;
using Viktor.Models;
using Viktor.ViewModels;
using Viktor.Views;

namespace Viktor.Tests;

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
    public void A_place_with_nothing_on_it_is_not_taken()
    {
        Ui.Run(() =>
        {
            var window = new ElementPickerWindow((x, y) => null);

            window.Show();
            Dispatcher.UIThread.RunJobs();

            window.ProbeAt(300, 200);
            Assert.Null(window.Hovered);

            // Nothing worth writing, so the picker stays up and the pointer can be moved on.
            window.Confirm();
            Assert.Null(window.Chosen);
            Assert.True(window.IsVisible);

            window.Close();
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
            .Where(definition => definition.Parameters.Any(parameter => parameter.Name == "selector"))
            .Select(definition => definition.Key)
            .Order()
            .ToList());

        Assert.Equal(
            ["condition.uiaExists", "uia.click", "uia.exists", "uia.getText", "uia.setText",
             "uia.waitElement"],
            keys);
    }

    [Fact]
    public void A_selector_is_edited_with_the_element_picker()
    {
        Ui.Run(() =>
        {
            var definition = ActionCatalog.Find("uia.click")
                ?? throw new InvalidOperationException("uia.click is missing from the catalogue.");

            var selector = new StepParameterViewModel(
                definition.Parameters.First(parameter => parameter.Name == "selector"));
            var window = new StepParameterViewModel(
                definition.Parameters.First(parameter => parameter.Name == "window"));

            Assert.True(selector.IsText);
            Assert.True(selector.IsSelector);

            // The bare one-line field belongs to every other text parameter, so the selector is
            // never drawn twice on top of itself.
            Assert.False(selector.IsPlainText);
            Assert.True(window.IsPlainText);
            Assert.False(window.IsSelector);
        });
    }

    /// <summary>The control a test pretends the pointer was put on.</summary>
    private static UiElementInfo At(int x, int y) => new(
        "Save",
        "saveButton",
        "Button",
        "ButtonClass",
        new ScreenPoint(x, y),
        new ScreenSize(80, 24),
        "Untitled - Notepad");
}
