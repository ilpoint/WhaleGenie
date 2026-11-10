using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

// Elements of a window's own interface: finding, reading, pressing and writing them.
public partial class DeviceActionTests
{
    // ------------------------------------------------------------------ window controls

    [Fact]
    public async Task Looking_for_an_element_reports_true_or_false()
    {
        var found = new FakeDeviceLayer();
        found.Ui.ElementExists = true;
        var missing = new FakeDeviceLayer();
        missing.Ui.ElementExists = false;

        ExecutableStep Exists() => Step("uia.exists",
            Param("window", "Notepad"), Param("uiSelector", "Button[name='Save']"),
            Returns("exists=there"));

        var (_, _, yes) = await RunAsync([Exists()], found);
        var (_, _, no) = await RunAsync([Exists()], missing);

        Assert.Equal("true", yes.Local.Values["there"].AsText());
        Assert.Equal("false", no.Local.Values["there"].AsText());
        Assert.Contains("ui Button/Save/", found.Calls[0]);
    }

    /// <summary>
    /// 两个同名按钮里要右边那一个时说的一句话，要整句到设备才算数：挨着谁、哪一边、至少隔开
    /// 多少。设备按这三样筛，引擎不替它猜。
    /// </summary>
    [Fact]
    public async Task An_element_anchor_reaches_the_device_with_its_side_and_gap()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync(
        [
            Step("uia.click", Param("uiSelector", "Button[name='保存']"),
                Param("anchorElement", "Button[name='确定']"),
                Param("anchorSide", "right"), Param("anchorGap", "40")),
        ], devices);

        var anchor = Assert.IsType<UiAnchor>(devices.Ui.LastQuery!.Anchor);
        Assert.Equal(UiSide.Right, anchor.Side);
        Assert.Equal(40, anchor.Gap);
        Assert.Equal("确定", anchor.Query.Name);
        Assert.Equal("Button", anchor.Query.ControlType);
        Assert.Null(anchor.Query.Anchor);
    }

    /// <summary>没有锚点的那一格空着，就是不要锚点，不是「间隔为零的锚点」。</summary>
    [Fact]
    public async Task A_step_without_an_anchor_asks_for_none()
    {
        var devices = new FakeDeviceLayer();

        await RunAsync([Step("uia.click", Param("uiSelector", "Button[name='保存']"))], devices);

        Assert.Null(devices.Ui.LastQuery!.Anchor);
    }

    /// <summary>
    /// 哪一边写了目录之外的词要在设备之前就拦下：放过它，这一步会落到另一个元素上，而且不说。
    /// </summary>
    [Fact]
    public async Task An_anchor_side_the_catalogue_does_not_know_stops_the_step()
    {
        var devices = new FakeDeviceLayer();

        var (result, _, _) = await RunAsync(
        [
            Step("uia.click", Param("uiSelector", "Button[name='保存']"),
                Param("anchorElement", "Button[name='确定']"), Param("anchorSide", "diagonal")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadOption", result.Key);
        Assert.Null(devices.Ui.LastQuery);
    }

    /// <summary>
    /// 「还要像什么样」从步骤里的一行行读成设备要的条件：哪一种、要不要一段字。选择器说它叫
    /// 什么，这里说它在不在屏幕上、能不能用、名字里有没有一段字。没写完的行与要字却没写的行
    /// 都不算数，与网页那边同一套读法。
    /// </summary>
    [Fact]
    public async Task The_conditions_an_element_has_to_meet_reach_the_device()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("uia.exists", Param("window", "Notepad"),
                Param("uiSelector", "Button[name='保存']"),
                Rows("narrow", Narrow("Visible"), Narrow("Enabled"), Narrow("NameHas", "存"),
                    Narrow("NameHas"), Narrow("")),
                Returns("exists=there")),
        ], devices);

        Assert.True(result.Succeeded, $"{result.Key}: {result.Detail}");
        var asked = devices.Ui.LastQuery!;
        Assert.Equal(
            [UiConditionHow.Visible, UiConditionHow.Enabled, UiConditionHow.NameHas],
            asked.Conditions.Select(condition => condition.How));
        Assert.Equal("存", asked.Conditions[2].Value);
    }

    /// <summary>
    /// 一个引擎不认识的限定方式要当场报出来：少了一个条件，这一步会落到另一个元素上，而且不说。
    /// </summary>
    [Fact]
    public async Task A_condition_the_engine_does_not_know_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("uia.exists", Param("window", "Notepad"),
                Param("uiSelector", "Button[name='保存']"),
                Rows("narrow", Narrow("Colour", "red")),
                Returns("exists=there")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementBadCondition", result.Key);
    }

    /// <summary>
    /// 一行限定都不写时，设备收到的查询与没有这一格时完全一样：这一格是加出来的，不是换掉的。
    /// </summary>
    [Fact]
    public async Task A_step_with_no_conditions_asks_for_none()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("uia.exists", Param("window", "Notepad"),
                Param("uiSelector", "Button[name='保存']"),
                Returns("exists=there")),
        ], devices);

        Assert.Empty(devices.Ui.LastQuery!.Conditions);
    }

    [Fact]
    public async Task Waiting_for_an_element_carries_on_when_it_is_there()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementExists = true;
        var (result, _, _) = await RunAsync(
        [
            Within(2000, Step("uia.waitElement",
                Param("window", "Notepad"),
                Param("uiSelector", "Button[name='Save']"),
                Param("pollMs", "1"))),
        ], devices);

        Assert.True(result.Succeeded, $"{result.Key}: {result.Detail}");
        Assert.Contains("ui Button/Save/", devices.Calls);
    }

    /// <summary>
    /// What turned up is handed over, in the same shape reading an element hands it over: a step that
    /// waits for a box and then reads it asks the same question twice, and the second answer can be a
    /// different element from the one that ended the wait.
    /// </summary>
    [Fact]
    public async Task Waiting_for_an_element_hands_over_the_one_that_turned_up()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementExists = true;
        devices.Ui.ElementText = "12 items";
        devices.Ui.Elements.Add(new UiElementInfo("Count", "countText", "Text", "TextClass",
            new ScreenPoint(10, 20), new ScreenSize(80, 24), "Untitled - Notepad"));

        var (result, _, store) = await RunAsync(
        [
            Within(2000, Step("uia.waitElement",
                Param("window", "Notepad"),
                Param("uiSelector", "Text[name='Count']"),
                Returns("element=found"))),
        ], devices);

        Assert.True(result.Succeeded, $"{result.Key}: {result.Detail}");
        Assert.Equal("12 items", Field(store, "found", "value").AsText());
        Assert.Equal("countText", Field(store, "found", "automationId").AsText());
        Assert.Equal("10,20,80,24", Field(store, "found", "box").AsText());
    }

    /// <summary>
    /// One element is one record, the same shape a window and a hit come in: what it holds, what it
    /// is called, whether it can be used, and where it sits, all under names.
    /// </summary>
    [Fact]
    public async Task Reading_an_element_hands_over_one_record_about_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementText = "12 items";
        devices.Ui.ElementEnabled = false;
        devices.Ui.Elements.Add(new UiElementInfo("Count", "countText", "Text", "TextClass",
            new ScreenPoint(10, 20), new ScreenSize(80, 24), "Untitled - Notepad"));

        var (_, _, store) = await RunAsync(
        [
            Step("uia.read", Param("window", ""), Param("uiSelector", "Text[name='Count']"),
                Returns("element=found")),
        ], devices);

        Assert.Equal("12 items", Field(store, "found", "value").AsText());
        Assert.Equal("Count", Field(store, "found", "name").AsText());
        Assert.Equal("countText", Field(store, "found", "automationId").AsText());
        Assert.False(Field(store, "found", "enabled").AsBool());
        Assert.True(Field(store, "found", "visible").AsBool());
        Assert.Equal("10,20,80,24", Field(store, "found", "box").AsText());
        Assert.Equal(80, Field(store, "found", "width").AsNumber());

        // 元素记录也带着它的中心，与命中同一条算法："点这个按钮正中"是一格字段。
        Assert.Equal("50,32", Centre(store, "found"));
    }

    [Fact]
    public async Task Reading_an_element_that_is_not_there_is_refused()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementExists = false;

        var (result, _, store) = await RunAsync(
        [
            Step("uia.read", Param("window", ""), Param("uiSelector", "Text[name='Count']"),
                Returns("element=found")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementNotFound", result.Key);
        Assert.Equal(string.Empty, store.Local.Values["found"].AsText());
    }

    /// <summary>
    /// Pressing an element is the way to work a control without the mouse: the pointer stays where
    /// it was and the window is not brought forward, which is what a macro driving a background
    /// window needs.
    /// </summary>
    [Fact]
    public async Task Pressing_an_element_works_it_without_the_mouse()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementExists = true;
        var (result, _, _) = await RunAsync(
        [
            Step("uia.invoke", Param("window", "Notepad"), Param("uiSelector", "Button[name='Save']")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("invoke Save #1", devices.Calls);

        // Nothing was clicked and no key was sent: the element was worked, not aimed at.
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("clickElement", StringComparison.Ordinal));
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("keyPress", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pressing_an_element_that_cannot_be_worked_that_way_is_refused()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementInvocable = false;

        var (result, _, _) = await RunAsync(
        [
            Step("uia.invoke", Param("window", "Notepad"), Param("uiSelector", "Button[name='Save']")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementNotInvocable", result.Key);
    }

    [Fact]
    public async Task Finding_an_element_records_where_it_sits_and_which_window_it_is_in()
    {
        var devices = WithAWindow();
        devices.Ui.Elements.Add(new UiElementInfo("Save", "saveButton", "Button", "ButtonClass",
            new ScreenPoint(10, 20), new ScreenSize(80, 24), "Untitled - Notepad"));

        var (_, _, store) = await RunAsync(
        [
            Step("uia.find", Param("window", "Notepad"), Param("uiSelector", "Button[name='Save']"),
                Returns("match=save", "window=source")),
        ], devices);

        // The centre is what a click aims at, and the parts are what a later step measures from.
        Assert.Equal("50,32", Centre(store, "save"));
        Assert.Equal("50", Field(store, "save", "x").AsText());
        Assert.Equal("32", Field(store, "save", "y").AsText());
        Assert.Equal("80", Field(store, "save", "width").AsText());
        Assert.Equal("24", Field(store, "save", "height").AsText());
        Assert.Equal("Save", Field(store, "save", "text").AsText());
        Assert.Equal("Notepad - notes.txt", store.Local.Values["source"].AsText());
    }

    [Fact]
    public async Task A_find_that_hits_nothing_empties_the_variable()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("uia.find", Param("uiSelector", "Button[name='Save']"),
                Hit("match", "save", every: true)),
        ], devices);

        // Nothing was found, so the record is empty and the list of hits is empty too: a step that
        // found nothing hands over nothing rather than the place the pass before it found.
        Assert.Equal(string.Empty, store.Local.Values["save"].AsText());
        Assert.Empty(store.Local.Values["save.every"].AsList());
    }

    [Fact]
    public async Task Recording_every_element_notes_how_many_and_where()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.Elements.Add(new UiElementInfo("Row 1", "row1", "ListItem", "ListBoxItem",
            new ScreenPoint(0, 100), new ScreenSize(200, 20), "Tasks"));
        devices.Ui.Elements.Add(new UiElementInfo("Row 2", "row2", "ListItem", "ListBoxItem",
            new ScreenPoint(0, 120), new ScreenSize(200, 20), "Tasks"));

        var (_, _, store) = await RunAsync(
        [
            Step("uia.find", Param("uiSelector", "ListItem[automationId='row1']"),
                Hit("match", "rows", every: true)),
        ], devices);

        Assert.Equal(2, store.Local.Values["rows.every"].AsList().Count);
        Assert.Equal("100,110, 100,130", Centres(store, "rows"));
    }

    [Fact]
    public async Task The_match_number_asks_the_search_for_that_one()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("uia.click", Param("uiSelector", "ListItem[automationId='row1']"),
                Param("matchIndex", "3")),
        ], devices);

        Assert.Contains("clickElement  #3 left", devices.Calls);
    }

    [Fact]
    public async Task Picking_an_entry_says_which_one_and_how()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("uia.select", Param("uiSelector", "ComboBox[automationId='rate']"),
                Param("item", "Fast"), Param("itemIndex", "0")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("selectItem ComboBox//rate#1 \"Fast\" 0", devices.Calls);
    }

    [Fact]
    public async Task Picking_an_entry_by_number_counts_from_one()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("control.setVariable", Param("name", "wanted"), Param("value", "Fast")),
            Step("uia.select", Param("uiSelector", "ComboBox[automationId='rate']"),
                Param("item", "$wanted"), Param("itemIndex", "3")),
        ], devices);

        // The text field is kept as written even when the number is what decides, so a step can
        // be switched from one to the other without losing what was typed; a variable in it is
        // filled in the way every other field reads one.
        Assert.Contains("selectItem ComboBox//rate#1 \"Fast\" 3", devices.Calls);
    }

    [Fact]
    public async Task A_pick_with_nothing_to_pick_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [Step("uia.select", Param("uiSelector", "ComboBox[automationId='rate']"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.MissingItem", result.Key);
    }

    [Fact]
    public async Task A_pick_that_finds_no_such_entry_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ItemSelectable = false;
        var (result, _, _) = await RunAsync(
        [
            Step("uia.select", Param("uiSelector", "ComboBox[automationId='rate']"),
                Param("item", "Missing")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ItemNotFound", result.Key);
    }

    [Fact]
    public async Task A_check_box_can_be_switched_by_name_or_flipped()
    {
        var devices = new FakeDeviceLayer();
        await RunAsync(
        [
            Step("uia.check", Param("uiSelector", "CheckBox[name='Remember']"), Param("checkState", "off")),
            Step("uia.check", Param("uiSelector", "CheckBox[name='Remember']"), Param("checkState", "toggle")),
        ], devices);

        Assert.Contains("setChecked Remember False", devices.Calls);
        Assert.Contains("setChecked Remember flip", devices.Calls);
    }

    [Fact]
    public async Task Opening_and_closing_a_node_says_which_way()
    {
        var devices = new FakeDeviceLayer();
        var (result, _, _) = await RunAsync(
        [
            Step("uia.expand", Param("uiSelector", "TreeItem[name='Tools']"),
                Param("expandState", "collapse")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("setExpanded Tools collapse", devices.Calls);
    }

    [Fact]
    public async Task Scrolling_into_view_a_step_of_a_list_that_will_not_scroll_fails()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.Scrollable = false;
        var (result, _, _) = await RunAsync(
        [Step("uia.scrollIntoView", Param("uiSelector", "ListItem[name='Row 9']"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementNotScrollable", result.Key);
    }

    [Fact]
    public async Task Reading_a_table_puts_the_rows_in_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.Table.Add(["Name", "Age"]);
        devices.Ui.Table.Add(["Ann", "31"]);

        var (result, _, store) = await RunAsync(
        [
            Step("uia.readTable", Param("uiSelector", "Table[automationId='people']"),
                Returns("table=people")),
        ], devices);

        Assert.True(result.Succeeded);

        // The shape is the same as the CSV reader's: one record per row, a field per cell. This grid
        // keeps no strip of column names, so the places its cells sit in key them: A, B.
        var table = store.Local.Values["people"];
        Assert.True(table.IsList);
        Assert.Equal(2, table.Items.Count);
        Assert.Equal(["A", "B"], table.Items[0].Fields.Select(field => field.Name));
        Assert.Equal("Name", table.Items[0].Field("A")!.AsText());
        Assert.Equal("31", table.Items[1].Field("B")!.AsText());
    }

    [Fact]
    public async Task Reading_a_table_stops_at_the_row_count_it_was_given()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.Table.Add(["one"]);
        devices.Ui.Table.Add(["two"]);
        devices.Ui.Table.Add(["three"]);

        var (_, _, store) = await RunAsync(
        [
            Step("uia.readTable", Param("uiSelector", "Table[automationId='people']"),
                Param("maxRows", "2"), Returns("table=people")),
        ], devices);

        Assert.Equal(2, store.Local.Values["people"].Items.Count);
    }

    /// <summary>
    /// A table keeps the names of its columns in a strip of its own, which is not a row of data, and
    /// they are what every row is keyed by: a cell is read by the name a person sees rather than by
    /// counting along the line, which is how a table comes to be read out of the wrong column
    /// without anything saying so.
    /// </summary>
    [Fact]
    public async Task Reading_a_table_keys_every_row_by_its_column_names()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.Columns.AddRange(["Name", "Age"]);
        devices.Ui.Table.Add(["Ann", "31"]);

        var (result, _, store) = await RunAsync(
        [
            Step("uia.readTable", Param("uiSelector", "Table[automationId='people']"),
                Returns("table=people")),
        ], devices);

        Assert.True(result.Succeeded);

        // The title strip is not a row: the one row that came back is the data, and its keys are
        // the names written above its columns.
        var row = Assert.Single(store.Local.Values["people"].AsList());
        Assert.Equal(["Name", "Age"], row.Fields.Select(field => field.Name));
        Assert.Equal("Ann", row.Field("Name")!.AsText());
        Assert.Equal("31", row.Field("Age")!.AsText());
    }

    /// <summary>
    /// A strip with a gap in it, or one that writes the same name twice, cannot key two cells the
    /// same way: the column nobody named, and the one repeating a name already taken, keep the
    /// letter of their place rather than putting a cell out of reach.
    /// </summary>
    [Fact]
    public async Task A_blank_or_repeated_column_name_takes_the_letter_of_its_place()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.Columns.AddRange(["Name", "", "Name"]);
        devices.Ui.Table.Add(["Ann", "31", "x"]);

        var (_, _, store) = await RunAsync(
        [
            Step("uia.readTable", Param("uiSelector", "Table[automationId='people']"),
                Returns("table=people")),
        ], devices);

        var row = Assert.Single(store.Local.Values["people"].AsList());
        Assert.Equal(["Name", "B", "C"], row.Fields.Select(field => field.Name));
        Assert.Equal("31", row.Field("B")!.AsText());
        Assert.Equal("x", row.Field("C")!.AsText());
    }

    [Fact]
    public async Task Measuring_from_a_control_is_not_one_of_the_coordinate_bases()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.Elements.Add(new UiElementInfo("Panel", "panel", "Pane", "PaneClass",
            new ScreenPoint(200, 100), new ScreenSize(400, 300), "Tasks"));

        var (result, _, _) = await RunAsync(
        [
            Step("input.mouseMove", Param("x", "10"), Param("y", "20"),
                Param("anchorMode", "element"), Param("anchorSelector", "Pane[automationId='panel']")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadOption", result.Key);
        Assert.DoesNotContain("findElements Pane//panel #1 take 1", devices.Calls);
    }

    [Fact]
    public async Task Writing_into_an_element_carries_the_text_and_the_clear_flag()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementWritable = true;
        var (result, _, _) = await RunAsync(
        [
            Step("uia.setText", Param("window", ""), Param("uiSelector", "Edit[automationId='input']"),
                Param("valueText", "hello"), Param("clearFirst", "false")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("fill Edit/input hello False", devices.Calls);
    }

    [Fact]
    public async Task An_element_that_cannot_be_written_fails_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementWritable = false;
        var (result, _, _) = await RunAsync(
        [
            Step("uia.setText", Param("uiSelector", "Edit[name='Query']"), Param("valueText", "hello")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.ElementNotWritable", result.Key);
    }

    [Fact]
    public async Task Focusing_a_window_that_is_not_there_fails()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.WindowFocused = false;
        var (result, _, _) = await RunAsync(
            [Step("uia.focusWindow", Param("window", "Nothing"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.WindowNotFound", result.Key);
    }

    [Fact]
    public async Task A_selector_reads_as_a_control_and_its_properties()
    {
        var devices = new FakeDeviceLayer();
        devices.Ui.ElementExists = true;
        await RunAsync(
        [
            Step("uia.exists", Param("window", "Notepad"),
                Param("uiSelector", "Edit[automationId='input', controlType='Edit', class='Edit']"),
                Returns("exists=there")),
        ], devices);

        Assert.Contains("ui Edit//input", devices.Calls[0]);
    }
}
