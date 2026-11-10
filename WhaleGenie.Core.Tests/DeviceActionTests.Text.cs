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

// Recognising text: where it was read, how it is filtered, and what clicking it does.
public partial class DeviceActionTests
{
    // ------------------------------------------------------------------ text on screen

    [Fact]
    public async Task Recognising_text_puts_the_words_in_a_variable()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("Ready", new ScreenPoint(1, 2), new ScreenSize(10, 4), 0.9),
                new TextSpan("Go", new ScreenPoint(20, 2), new ScreenSize(6, 4), 0.8),
            ];

        var (result, _, store) = await RunAsync(
        [
            Recognize(0, 0, 100, 20, Returns("text=words")),
        ], devices);

        Assert.True(result.Succeeded, $"{result.Key}: {result.Detail}");
        Assert.Equal("Ready Go", store.Local.Values["words"].AsText());
        Assert.Contains("ocr", devices.Calls);
    }

    [Fact]
    public async Task Finding_text_reports_where_it_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("Save as", new ScreenPoint(10, 10), new ScreenSize(20, 8), 0.9)];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Save"), Param("region", ""),
                Param("matchMode", "contains"), Hit("match", "spot")),
        ], devices);

        Assert.Equal("20,14", Centre(store, "spot"));
    }

    /// <summary>
    /// A screen can show the same word in two cases. Turning case sensitivity on leaves only the
    /// reading that spells it the way the step asked, while the default still takes both.
    /// </summary>
    [Fact]
    public async Task A_text_search_can_keep_letter_case()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("Save", new ScreenPoint(10, 10), new ScreenSize(20, 8), 0.9),
                new TextSpan("save", new ScreenPoint(10, 40), new ScreenSize(20, 8), 0.9),
            ];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Save"), Param("matchMode", "equals"),
                Param("caseSensitive", "true"), Hit("match", "exact", every: true)),
            Step("ocr.findText", Param("queryText", "Save"), Param("matchMode", "equals"),
                Hit("match", "any", every: true)),
        ], devices);

        Assert.Single(store.Local.Values["exact.every"].AsList());
        Assert.Equal(2, store.Local.Values["any.every"].AsList().Count);
        Assert.Equal("20,14", Centre(store, "exact"));
    }

    [Fact]
    public async Task A_text_search_can_leave_out_a_reading_the_model_was_unsure_of()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("Save as", new ScreenPoint(10, 10), new ScreenSize(20, 8), 12),
                new TextSpan("Save now", new ScreenPoint(10, 40), new ScreenSize(20, 8), 38),
            ];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Save"), Param("matchMode", "contains"),
                Param("minScore", "30"), Hit("match", "sure", every: true)),
            Step("ocr.findText", Param("queryText", "Save"), Param("matchMode", "contains"),
                Hit("match", "any")),
        ], devices);

        // The shaky reading is dropped, so the only one left is the one the model was sure of —
        // including in the list of every hit.
        Assert.Equal("20,44", Centre(store, "sure"));
        Assert.Single(store.Local.Values["sure.every"].AsList());

        // Left alone, the field takes everything, shaky reading first, which is what macros that
        // never set it have always seen.
        Assert.Equal("20,14", Centre(store, "any"));
    }

    /// <summary>
    /// A screen full of the same word is narrowed down by the shape the reading has to have: the
    /// level and not the label, the amount and not the heading.
    /// </summary>
    [Fact]
    public async Task A_text_search_can_insist_on_the_shape_of_what_it_finds()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("HP full", new ScreenPoint(0, 0), new ScreenSize(40, 8), 40),
                new TextSpan("HP 120/300", new ScreenPoint(0, 20), new ScreenSize(40, 8), 40),
            ];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "HP"), Param("expected", @"^HP \d+/\d+$"),
                Hit("match", "where")),
        ], devices);

        Assert.Equal("20,24", Centre(store, "where"));
    }

    /// <summary>A pattern nothing can read is the macro's own mistake, so it is said in a sentence.</summary>
    [Fact]
    public async Task A_shape_that_makes_no_sense_fails_the_step_with_a_sentence()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("HP 120/300", new ScreenPoint(0, 0), new ScreenSize(40, 8), 40)];

        var (result, _, _) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "HP"), Param("expected", @"HP (\d+"),
                Hit("match", "where")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadPattern", result.Key);
    }

    /// <summary>
    /// A model that keeps misreading the same thing is a mistake the user can see; writing it down
    /// on the step is what keeps a macro working without a screen that reads cleanly.
    /// </summary>
    [Fact]
    public async Task A_reading_the_model_keeps_getting_wrong_can_be_put_right()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("G0LD l00", new ScreenPoint(0, 0), new ScreenSize(40, 8), 40)];

        var (result, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "GOLD 100"), Param("matchMode", "equals"),
                Param("fixText", "G0LD = GOLD\nl00 = 100"), Hit("match", "where")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Equal("20,4", Centre(store, "where"));
        Assert.Equal("GOLD 100", Field(store, "where", "text").AsText());
    }

    /// <summary>
    /// Writing drawn with an outline or over a picture is where the model is handed too much:
    /// saying what colour the writing is in hands it the writing.
    /// </summary>
    [Fact]
    public async Task Writing_can_be_handed_over_in_its_own_colour_alone()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FFFFFF,#00FF00");
        devices.Ocr.Spans = [new TextSpan("42", new ScreenPoint(0, 0), new ScreenSize(2, 1), 40)];

        var (_, _, _) = await RunAsync(
        [
            Recognize(0, 0, 2, 1, Param("colorFilter", "#FFFFFF"),
                Param("colorTolerance", "10"), Returns("text=text")),
        ], devices);

        var handed = Assert.Single(devices.Ocr.OcrPictures);

        // The green pixel is not the writing, so it arrives flat white; the white one is.
        Assert.Equal(255, handed[0, 0].R);
        Assert.Equal(255, handed[1, 0].R);
        Assert.Equal(255, handed[1, 0].G);
        Assert.Equal(255, handed[1, 0].B);
    }

    /// <summary>Left empty, the picture reaches the reading model exactly as it was captured.</summary>
    [Fact]
    public async Task Without_a_colour_asked_for_the_picture_is_read_as_it_is()
    {
        var devices = new FakeDeviceLayer();
        devices.Screen.Display = Picture("#FFFFFF,#00FF00");
        devices.Ocr.Spans = [new TextSpan("42", new ScreenPoint(0, 0), new ScreenSize(2, 1), 40)];

        var (_, _, _) = await RunAsync(
        [
            Recognize(0, 0, 2, 1, Returns("text=text")),
        ], devices);

        var handed = Assert.Single(devices.Ocr.OcrPictures);
        Assert.Equal(0, handed[1, 0].R);
        Assert.Equal(255, handed[1, 0].G);
    }

    [Fact]
    public async Task A_text_search_can_record_every_place_the_text_was_read()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("Total 12", new ScreenPoint(0, 0), new ScreenSize(40, 8), 0.9),
                new TextSpan("Other", new ScreenPoint(0, 20), new ScreenSize(20, 8), 0.9),
                new TextSpan("Total 99", new ScreenPoint(0, 40), new ScreenSize(40, 8), 0.9),
            ];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Total"), Param("matchMode", "contains"),
                Hit("match", "totals", every: true)),
            Step("ocr.findText", Param("queryText", "Other"), Param("matchMode", "contains"),
                Hit("match", "one")),
        ], devices);

        Assert.Equal("20,4", Centre(store, "totals"));
        Assert.Equal(2, store.Local.Values["totals.every"].AsList().Count);
        Assert.Equal("20,4, 20,44", Centres(store, "totals"));

        // A step that did not ask for the whole set leaves no list behind for an older macro to
        // trip over.
        Assert.False(store.Local.Values.ContainsKey("one.every"));
    }

    /// <summary>
    /// A search that records every place records what each of them said as well, in the same order:
    /// a macro that walks the places usually wants to know which of them said what, and a list of
    /// places on its own cannot say it.
    /// </summary>
    [Fact]
    public async Task A_text_search_that_records_every_place_records_what_each_one_said()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("Total 12", new ScreenPoint(0, 0), new ScreenSize(40, 8), 0.9),
                new TextSpan("Other", new ScreenPoint(0, 20), new ScreenSize(20, 8), 0.9),
                new TextSpan("Total 99", new ScreenPoint(0, 40), new ScreenSize(40, 8), 0.9),
            ];

        var (result, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Total"), Param("matchMode", "contains"),
                Returns("match=one", "every=one.every")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(2, store.Local.Values["one.every"].AsList().Count);

        // Where each of them was and what it said are the two halves of one record, so the list of
        // hits reads as one list rather than as two that have to be lined up by counting.
        Assert.Equal("20,4, 20,44", Centres(store, "one"));
        Assert.Equal("Total 12", store.Local.Values["one.every"].AsList()[0].Field("text")!.AsText());
        Assert.Equal("Total 99", store.Local.Values["one.every"].AsList()[1].Field("text")!.AsText());
    }

    /// <summary>
    /// A step that clicks what it read hands the reading over the way the step that only looks for
    /// text does, so where it clicked can be read afterwards.
    /// </summary>
    [Fact]
    public async Task A_click_on_a_line_hands_over_where_it_clicked()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("Save", new ScreenPoint(10, 10), new ScreenSize(20, 8), 0.9)];

        var (result, _, store) = await RunAsync(
        [
            Step("ocr.clickText", Param("queryText", "Save"), Param("matchMode", "contains"),
                Returns("match=spot")),
        ], devices);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("20,14", Centre(store, "spot"));
        Assert.Equal("Save", Field(store, "spot", "text").AsText());
        Assert.Equal(20, Field(store, "spot", "x").AsNumber());
    }

    [Fact]
    public async Task Reading_and_finding_can_ask_for_the_numbers_only()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("合计 ¥1,234.50", new ScreenPoint(0, 0), new ScreenSize(80, 12), 0.9),
                new TextSpan("已完成", new ScreenPoint(0, 20), new ScreenSize(30, 12), 0.9),
            ];

        var (_, _, store) = await RunAsync(
        [
            Recognize(0, 0, 100, 40, Param("content", "digits"), Returns("text=amounts")),
            Step("ocr.findText", Param("queryText", "1234.50"), Param("content", "digits"),
                Hit("match", "where")),
        ], devices);

        Assert.Equal("1234.50", store.Local.Values["amounts"].AsText());
        Assert.Equal("40,6", Centre(store, "where"));
    }

    /// <summary>
    /// The other kinds of writing a step can keep, which is how one screen full of labels is read
    /// for the code on it and for the Chinese beside it by the same kind of step. A piece left with
    /// none of what was asked for does not travel on as an empty reading.
    /// </summary>
    [Fact]
    public async Task Recognising_text_can_keep_only_one_kind_of_writing()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("序号 SN-2048 完成", new ScreenPoint(0, 0), new ScreenSize(120, 12), 0.9),
                new TextSpan("1234", new ScreenPoint(0, 20), new ScreenSize(30, 12), 0.8),
            ];

        var (_, _, store) = await RunAsync(
        [
            Recognize(0, 0, 100, 40, Param("content", "letters"), Returns("text=letters")),
            Recognize(0, 0, 100, 40, Param("content", "chinese"), Returns("text=chinese")),
            Recognize(0, 0, 100, 40, Param("content", "lettersAndChinese"),
                Returns("text=writing")),
        ], devices);

        Assert.Equal("SN", store.Local.Values["letters"].AsText());
        Assert.Equal("序号 完成", store.Local.Values["chinese"].AsText());
        Assert.Equal("序号 SN 完成", store.Local.Values["writing"].AsText());
    }

    /// <summary>
    /// Keeping what a pattern picks out, reading and extracting in one step: the amount inside a
    /// line of labels is what comes out, and the line the pattern missed is left out.
    /// </summary>
    [Fact]
    public async Task Recognising_text_can_keep_only_what_a_pattern_picks_out()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("合计 ¥1,234.50", new ScreenPoint(0, 0), new ScreenSize(90, 12), 0.9),
                new TextSpan("已发货", new ScreenPoint(0, 20), new ScreenSize(30, 12), 0.8),
                new TextSpan("订单号 ORD-88231", new ScreenPoint(0, 40), new ScreenSize(110, 12), 0.7),
            ];

        var (_, _, store) = await RunAsync(
        [
            Recognize(0, 0, 100, 60, Param("content", "regex"),
                Param("contentPattern", @"¥([\d,.]+)"), Returns("text=amount")),
            Recognize(0, 0, 100, 60, Param("content", "regex"),
                Param("contentPattern", @"ORD-\d+"), Returns("text=code")),
        ], devices);

        Assert.Equal("1,234.50", store.Local.Values["amount"].AsText());
        Assert.Equal("ORD-88231", store.Local.Values["code"].AsText());
    }

    /// <summary>
    /// The looking-for side reads the screen the same way, so a step can say what it wants in the
    /// words the screen actually shows: "SN" is not part of "SN-2048" until the reading is cut down
    /// to its letters.
    /// </summary>
    [Fact]
    public async Task Finding_text_reads_the_screen_the_way_the_step_said_to()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("SN-2048", new ScreenPoint(0, 0), new ScreenSize(40, 10), 0.9)];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "SN"), Param("matchMode", "equals"),
                Param("content", "letters"), Hit("match", "where")),
        ], devices);

        Assert.Equal("20,5", Centre(store, "where"));
    }

    /// <summary>
    /// A pattern the machine cannot read is the macro's problem to see, and it is seen as a failed
    /// step rather than as a screen that was read and then quietly kept whole.
    /// </summary>
    [Fact]
    public async Task A_pattern_the_text_reading_cannot_read_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("订单号 ORD-88231", new ScreenPoint(0, 0), new ScreenSize(110, 12), 0.9)];

        var (result, _, store) = await RunAsync(
        [
            Recognize(0, 0, 100, 20, Param("content", "regex"),
                Param("contentPattern", "ORD-[0-9"), Returns("text=code")),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.BadPattern", result.Key);
        Assert.Equal(string.Empty, store.Local.Values["code"].AsText());
    }

    /// <summary>
    /// A pattern left empty asks for no extracting: what was read stays as it was read, which is what
    /// the field being empty means everywhere else.
    /// </summary>
    [Fact]
    public async Task A_pattern_left_empty_keeps_everything_that_was_read()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("订单号 88231", new ScreenPoint(0, 0), new ScreenSize(90, 12), 0.9)];

        var (_, _, store) = await RunAsync(
        [
            Recognize(0, 0, 100, 20, Param("content", "regex"),
                Param("contentPattern", ""), Returns("text=text")),
        ], devices);

        Assert.Equal("订单号 88231", store.Local.Values["text"].AsText());
    }

    [Fact]
    public async Task Recognising_text_can_keep_the_lines_and_columns_it_was_read_in()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [
                new TextSpan("名称", new ScreenPoint(0, 0), new ScreenSize(30, 14), 0.9),
                new TextSpan("数量", new ScreenPoint(120, 0), new ScreenSize(30, 14), 0.9),
                new TextSpan("苹果", new ScreenPoint(0, 30), new ScreenSize(30, 14), 0.9),
                new TextSpan("3", new ScreenPoint(120, 30), new ScreenSize(8, 14), 0.9),
            ];

        var (_, _, store) = await RunAsync(
        [
            Recognize(0, 0, 200, 60, Param("table", "true"),
                Returns("table=sheet", "text=sheet.text")),
        ], devices);

        var rows = store.Local.Values["sheet"].AsList();
        Assert.Equal(2, rows.Count);

        // A row is a record keyed by the place each cell sits in: a page read off the screen has no
        // strip of column names to key it by.
        Assert.Equal(["A", "B"], rows[0].Fields.Select(field => field.Name));
        Assert.Equal("名称", rows[0].Field("A")!.AsText());
        Assert.Equal("数量", rows[0].Field("B")!.AsText());
        Assert.Equal("苹果", rows[1].Field("A")!.AsText());
        Assert.Equal("3", rows[1].Field("B")!.AsText());

        // The writing is still there as text, one line per row, which is what a macro shows or
        // writes to a file.
        Assert.Equal("名称\t数量\n苹果\t3", store.Local.Values["sheet.text"].AsText());
    }

    [Fact]
    public async Task Cleaning_the_picture_up_before_reading_it_keeps_the_positions_on_screen()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("Go", new ScreenPoint(20, 10), new ScreenSize(40, 8), 0.9)];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Go"), Param("preprocess", "upscale"),
                Hit("match", "spot")),
        ], devices);

        // The picture the OCR was handed was the searched region at twice its size...
        Assert.Equal([(200, 100)], devices.Ocr.OcrFrames);

        // ...so what it read comes back halved, and the place still means screen pixels: the
        // writing sat at 20,10 and was 40 by 8, so its centre is 40,14 and comes back as 20,7.
        Assert.Equal("20,7", Centre(store, "spot"));
        Assert.Equal(20, Field(store, "spot", "width").AsNumber());
        Assert.Equal(4, Field(store, "spot", "height").AsNumber());
    }

    /// <summary>
    /// The ways of comparing text are the ones the whole program shares: exactly the text (spacing
    /// at either end aside), or matching it as a pattern, or containing it.
    /// </summary>
    [Fact]
    public async Task A_text_search_compares_the_way_the_step_says()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("Running", new ScreenPoint(0, 0), new ScreenSize(10, 4), 0.9)];

        var (_, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Run"), Param("matchMode", "equals"),
                Hit("match", "exactHit")),
            Step("ocr.findText", Param("queryText", "Run"), Param("matchMode", "startsWith"),
                Hit("match", "startHit")),
            Step("ocr.findText", Param("queryText", "ning"), Param("matchMode", "endsWith"),
                Hit("match", "endHit")),
            Step("ocr.findText", Param("queryText", "^Run"), Param("matchMode", "regex"),
                Hit("match", "patternHit")),
            Step("ocr.findText", Param("queryText", "nope"), Param("matchMode", "contains"),
                Hit("match", "miss")),
        ], devices);

        Assert.Equal(string.Empty, store.Local.Values["exactHit"].AsText());
        Assert.Equal("5,2", Centre(store, "startHit"));
        Assert.Equal("5,2", Centre(store, "endHit"));
        Assert.Equal("5,2", Centre(store, "patternHit"));
        Assert.Equal(string.Empty, store.Local.Values["miss"].AsText());
    }

    /// <summary>
    /// A way of comparing that is none of the five stops the step and says which word it could not
    /// read, rather than quietly comparing some other way — "exact" was a way once and now means
    /// nothing, and a macro that says it must be told so rather than reading as "contained in it".
    /// Leaving the field out is not a wrong word: it means the default, as it does everywhere.
    /// </summary>
    [Fact]
    public async Task A_match_mode_that_is_none_of_the_five_stops_the_step()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("Save the file", new ScreenPoint(0, 0), new ScreenSize(40, 8), 0.9)];

        var (refused, _, _) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "Save"), Param("matchMode", "exact"),
                Hit("match", "where")),
        ], devices);

        Assert.False(refused.Succeeded);
        Assert.Equal("Run.BadMatchMode", refused.Key);
        Assert.Equal("exact", refused.Detail);

        // Said nothing, which is what a field nobody filled in looks like: contained in it.
        var (asked, _, store) = await RunAsync(
        [
            Step("ocr.findText", Param("queryText", "the"), Hit("match", "where")),
        ], devices);

        Assert.True(asked.Succeeded);
        Assert.Equal("20,4", Centre(store, "where"));
    }

    [Fact]
    public async Task Clicking_text_clicks_where_the_words_are()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [new TextSpan("OK", new ScreenPoint(10, 20), new ScreenSize(20, 10), 0.9)];

        var (result, _, _) = await RunAsync(
        [
            Step("ocr.clickText", Param("queryText", "OK"), Param("matchMode", "contains"),
                Param("offsetX", "1"), Param("offsetY", "2"), Param("button", "left")),
        ], devices);

        Assert.True(result.Succeeded);
        Assert.Contains("click left 21 27 1 0", devices.Calls);
    }

    /// <summary>
    /// A wait that runs out says what it was waiting for: the step that never read its line names
    /// the line, because that is what the reader has to go and look at.
    /// </summary>
    [Fact]
    public async Task Clicking_text_that_never_shows_says_what_was_never_read()
    {
        var devices = new FakeDeviceLayer();
        devices.Ocr.Spans = [];

        var (result, _, _) = await RunAsync(
        [
            Within(20, Step("ocr.clickText", Param("queryText", "OK"))),
        ], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.TextNotFound", result.Key);
        Assert.DoesNotContain(devices.Calls, call => call.StartsWith("click ", StringComparison.Ordinal));
    }
}
