using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Writing a table by its column names rather than by counting columns, and writing the small
/// pieces a sheet is made of — one cell, one column of plain values — without building a table
/// around them first, plus making room for rows and taking them out. Reading is the other half of
/// this and lives in <c>SheetColumnsTests</c>.
/// </summary>
public class SheetWriteTests
{
    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>
    /// A write of the book these checks keep. The fields a caller names are the ones that count,
    /// and the rest keep the defaults a step gets in the editor.
    /// </summary>
    private static ExecutableStep Write(params ExecutableParameter[] parameters)
    {
        var given = parameters.Select(parameter => parameter.Name).ToHashSet();
        return new ExecutableStep
        {
            Type = "excel.writeSheet",
            Parameters =
            [
                .. new[]
                {
                    Param("path", "book.xlsx"),
                    Param("sheet", "Sheet1"),
                    Param("mode", "replace"),
                    Param("formula", "false"),
                }.Where(parameter => !given.Contains(parameter.Name)),
                .. parameters,
            ],
        };
    }

    /// <summary>Reads a whole sheet back as it sits, names and all, so cells can be counted.</summary>
    private static ExecutableStep Read(bool asText = false, string sheet = "Sheet1") => new()
    {
        Type = "excel.readSheet",
        Parameters =
        [
            Param("path", "book.xlsx"),
            Param("sheet", sheet),
            Param("hasHeader", "false"),
            Param("maxRows", "0"),
            Param("asText", asText ? "true" : "false"),
            Param("resultVariable", "back"),
        ],
    };

    /// <summary>One of the actions about the sheet itself: the workbook, then what the step says.</summary>
    private static ExecutableStep OnSheet(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = [Param("path", "book.xlsx"), .. parameters] };

    /// <summary>The workbook as it sits on the fake machine, which is where the sheet itself is read.</summary>
    private static XLWorkbook Book(FakeDeviceLayer devices, string path = "book.xlsx")
        => new(new MemoryStream(devices.Blobs[path]));

    private static async Task<RunResult> RunAsync(VariableStore store, FakeDeviceLayer devices,
        params ExecutableStep[] steps)
        => await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(steps);

    /// <summary>One of the row actions, which both take a sheet, a row and how many rows.</summary>
    private static ExecutableStep Rows(string type, int at, int count) => new()
    {
        Type = type,
        Parameters =
        [
            Param("path", "book.xlsx"),
            Param("sheet", "Sheet1"),
            Param("at", $"{at}"),
            Param("count", $"{count}"),
        ],
    };

    private static IReadOnlyList<IReadOnlyList<Value>> Rows(VariableStore store, string name)
        => [.. store.Local.Values[name].Items.Select(row => (IReadOnlyList<Value>)row.Items)];

    /// <summary>
    /// A sheet somebody else keeps: their columns sit in their own order, and the macro that fills
    /// it in has a table of its own.
    /// </summary>
    private static (VariableStore Store, FakeDeviceLayer Devices) TwoOrders()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("theirs", Value.FromList(
        [
            Value.FromList([Value.FromText("日期"), Value.FromText("订单号"), Value.FromText("金额")]),
            Value.FromList(
                [Value.FromText("2026-01-05"), Value.FromText("A123"), Value.FromNumber(120)]),
        ]));
        store.Local.Set("columns", Value.FromList(
            [Value.FromText("订单号"), Value.FromText("金额"), Value.FromText("日期")]));
        store.Local.Set("rows", Value.FromList(
        [
            Value.FromList(
                [Value.FromText("B456"), Value.FromNumber(80), Value.FromText("2026-01-06")]),
        ]));

        return (store, devices);
    }

    [Fact]
    public async Task A_single_value_goes_into_one_cell()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "120"), Param("mode", "insert"), Param("startCell", "B2")),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal(120, Assert.Single(Assert.Single(Rows(store, "back"))).AsNumber());
    }

    [Fact]
    public async Task A_plain_list_of_values_is_written_down_one_column()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("names", Value.FromList(
            [Value.FromText("a"), Value.FromText("b"), Value.FromText("c")]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$names")),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        // One cell per row, which is the shape reading one column hands back.
        Assert.Equal(["a", "b", "c"], Rows(store, "back").Select(row => row[0].AsText()));
    }

    [Fact]
    public async Task Aligning_puts_each_value_under_the_column_that_has_its_name()
    {
        var (store, devices) = TwoOrders();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$theirs")),
            Write(Param("rows", "$rows"), Param("header", "$columns"), Param("mode", "append"),
                Param("align", "true")),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        var back = Rows(store, "back");

        // Still one header: the second step's names say where its cells belong, they are not
        // written out again.
        Assert.Equal(3, back.Count);
        Assert.Equal(["日期", "订单号", "金额"], back[0].Select(cell => cell.AsText()));
        Assert.Equal("2026-01-06", back[2][0].AsText());
        Assert.Equal("B456", back[2][1].AsText());
        Assert.Equal(80, back[2][2].AsNumber());
    }

    [Fact]
    public async Task Aligning_while_replacing_keeps_the_names_the_sheet_had()
    {
        var (store, devices) = TwoOrders();

        // Only two of the three columns, and in the other order: the third column stays empty
        // rather than taking a value that belongs somewhere else.
        store.Local.Set("part", Value.FromList([Value.FromList([Value.FromText("A9"), Value.FromNumber(5)])]));
        store.Local.Set("partColumns", Value.FromList([Value.FromText("订单号"), Value.FromText("金额")]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$theirs")),
            Write(Param("rows", "$part"), Param("header", "$partColumns"), Param("align", "true")),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        var back = Rows(store, "back");
        Assert.Equal(["日期", "订单号", "金额"], back[0].Select(cell => cell.AsText()));
        Assert.Equal("", back[1][0].AsText());
        Assert.Equal("A9", back[1][1].AsText());
        Assert.Equal(5, back[1][2].AsNumber());
    }

    [Fact]
    public async Task Aligning_while_writing_over_fills_in_one_column_and_leaves_the_rest()
    {
        var (store, devices) = TwoOrders();
        store.Local.Set("amounts", Value.FromList(
            [Value.FromNumber(100), Value.FromNumber(200)]));
        store.Local.Set("amountColumn", Value.FromList([Value.FromText("金额")]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$theirs")),
            Write(Param("rows", "$amounts"), Param("header", "$amountColumn"), Param("mode", "insert"),
                Param("startCell", "A2"), Param("align", "true")),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        // The rows keep their dates and their numbers, and only the named column takes a value.
        var back = Rows(store, "back");
        Assert.Equal("2026-01-05", back[1][0].AsText());
        Assert.Equal("A123", back[1][1].AsText());
        Assert.Equal(100, back[1][2].AsNumber());
        Assert.Equal("", back[2][0].AsText());
        Assert.Equal(200, back[2][2].AsNumber());
    }

    [Fact]
    public async Task A_sheet_with_no_names_yet_takes_the_rows_in_the_order_they_are_written()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("columns", Value.FromList([Value.FromText("b"), Value.FromText("a")]));
        store.Local.Set("rows", Value.FromList([Value.FromList([Value.FromNumber(1), Value.FromNumber(2)])]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$rows"), Param("header", "$columns"), Param("align", "true")),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        var back = Rows(store, "back");
        Assert.Equal(["b", "a"], back[0].Select(cell => cell.AsText()));
        Assert.Equal([1d, 2d], back[1].Select(cell => cell.AsNumber()));
    }

    [Fact]
    public async Task A_name_the_sheet_does_not_have_is_reported_rather_than_guessed_at()
    {
        var (store, devices) = TwoOrders();
        store.Local.Set("wrong", Value.FromList([Value.FromText("总价")]));
        store.Local.Set("line", Value.FromList([Value.FromList([Value.FromNumber(1)])]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$theirs")),
            Write(Param("rows", "$line"), Param("header", "$wrong"), Param("mode", "append"),
                Param("align", "true")));

        Assert.Equal("Run.NoSuchColumn", result.Key);
    }

    [Fact]
    public async Task Aligning_a_cell_that_has_no_name_says_so()
    {
        var (store, devices) = TwoOrders();
        store.Local.Set("line", Value.FromList([Value.FromList([Value.FromNumber(1), Value.FromNumber(2)])]));
        store.Local.Set("one", Value.FromList([Value.FromText("订单号")]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$theirs")),
            Write(Param("rows", "$line"), Param("header", "$one"), Param("mode", "append"),
                Param("align", "true")));

        Assert.Equal("Run.AlignNeedsNames", result.Key);
    }

    [Fact]
    public async Task The_names_are_read_from_the_row_the_step_says_they_are_on()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("standing", Value.FromList(
            [Value.FromText("订单号"), Value.FromText("金额")]));

        // A report with a title, a blank row, and the table under it: the names are on row three.
        store.Local.Set("head", Value.FromList(
        [
            Value.FromList([Value.FromText("2026 年 1 月")]),
            Value.FromList([]),
            Value.FromList([Value.FromText("订单号"), Value.FromText("金额")]),
        ]));
        store.Local.Set("line", Value.FromList(
        [
            Value.FromList([Value.FromText("A1"), Value.FromNumber(10)]),
            Value.FromList([Value.FromText("A2"), Value.FromNumber(20)]),
        ]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$head")),
            Write(Param("rows", "$line"), Param("header", "$standing"), Param("mode", "append"),
                Param("align", "true"), Param("headerRow", "3")),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        // The title row and the gap under it are still there; the names are on row three, and the
        // rows went under them.
        var back = Rows(store, "back");
        Assert.Equal("2026 年 1 月", back[0][0].AsText());
        Assert.Equal(["订单号", "金额"], back[2].Select(cell => cell.AsText()));
        Assert.Equal("A1", back[3][0].AsText());
        Assert.Equal(10, back[3][1].AsNumber());
    }

    [Fact]
    public async Task Inserting_rows_makes_room_and_moves_the_rest_down()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("lines", Value.FromList(
        [
            Value.FromList([Value.FromText("a")]),
            Value.FromList([Value.FromText("b")]),
            Value.FromList([Value.FromText("c")]),
        ]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$lines")),
            Rows("excel.insertRows", 2, 1),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        // A blank row where the second one was, and the two under it one row further down.
        var back = Rows(store, "back");
        Assert.Equal(4, back.Count);
        Assert.Equal("a", back[0][0].AsText());
        Assert.Empty(back[1]);
        Assert.Equal("b", back[2][0].AsText());
        Assert.Equal("c", back[3][0].AsText());
    }

    [Fact]
    public async Task Deleting_rows_takes_them_out_and_brings_the_rest_up()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("lines", Value.FromList(
        [
            Value.FromList([Value.FromText("a")]),
            Value.FromList([Value.FromText("b")]),
            Value.FromList([Value.FromText("c")]),
            Value.FromList([Value.FromText("d")]),
        ]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$lines")),
            Rows("excel.deleteRows", 2, 2),
            Read());

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal(["a", "d"], Rows(store, "back").Select(row => row[0].AsText()));
    }

    [Fact]
    public async Task Deleting_more_rows_than_the_sheet_holds_takes_out_the_ones_it_does()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("lines", Value.FromList(
        [
            Value.FromList([Value.FromText("a")]),
            Value.FromList([Value.FromText("b")]),
        ]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$lines")),
            Rows("excel.deleteRows", 10, 5),
            Read());

        // Nothing there to take out is the same thing as a sheet that had already been cleared, so
        // a macro that clears a block runs again without stopping.
        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal(["a", "b"], Rows(store, "back").Select(row => row[0].AsText()));
    }

    [Fact]
    public async Task A_row_number_a_sheet_has_not_got_is_reported()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var wrong = await RunAsync(store, devices,
            Write(Param("rows", "a")),
            Rows("excel.insertRows", 0, 1));
        Assert.Equal("Run.BadRow", wrong.Key);

        var none = await RunAsync(store, devices,
            Write(Param("rows", "a")),
            Rows("excel.deleteRows", 1, 0));
        Assert.Equal("Run.BadRow", none.Key);
    }

    [Fact]
    public async Task Rows_go_into_the_sheet_the_step_names_and_a_sheet_that_is_not_there_is_reported()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "a")),
            new ExecutableStep
            {
                Type = "excel.insertRows",
                Parameters =
                [
                    Param("path", "book.xlsx"),
                    Param("sheet", "Nope"),
                    Param("at", "1"),
                    Param("count", "1"),
                ],
            });

        Assert.Equal("Run.NoSuchSheet", result.Key);
    }

    [Fact]
    public async Task A_column_shown_as_a_date_holds_dates()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("lines", Value.FromList(
        [
            Value.FromList([Value.FromText("2026-01-05")]),
            Value.FromList([Value.FromText("2026-01-06")]),
        ]));

        // The format is the other way round from the text that is written: a cell that only held
        // the text "2026-01-05" would show it back word for word.
        var result = await RunAsync(store, devices,
            Write(Param("rows", "$lines"), Param("numberFormat", "mm/dd/yyyy")),
            Read(asText: true));

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal(["01/05/2026", "01/06/2026"],
            Rows(store, "back").Select(row => row[0].AsText()));
    }

    [Fact]
    public async Task Text_that_is_not_a_date_is_left_alone_in_a_date_column()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("lines", Value.FromList(
        [
            Value.FromList([Value.FromText("订单-1")]),
            Value.FromList([Value.FromText("2026-01-05")]),
        ]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$lines"), Param("numberFormat", "yyyy-mm-dd")),
            Read(asText: true));

        // One is not a day and stays the text it is; the other is a day and comes back as one.
        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal(["订单-1", "2026-01-05"],
            Rows(store, "back").Select(row => row[0].AsText()));
    }

    [Fact]
    public async Task A_money_column_is_shown_with_its_decimals()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("amounts", Value.FromList([Value.FromNumber(120.5)]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$amounts"), Param("numberFormat", "#,##0.00")),
            Read(asText: true));

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal("120.50", Assert.Single(Assert.Single(Rows(store, "back"))).AsText());
    }

    [Fact]
    public async Task A_format_can_be_given_for_each_column_on_its_own()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();
        store.Local.Set("lines", Value.FromList(
        [
            Value.FromList([Value.FromText("2026-01-05"), Value.FromNumber(120.5)]),
        ]));
        store.Local.Set("formats", Value.FromList(
            [Value.FromText("yyyy-mm-dd"), Value.FromText("#,##0.00")]));

        var result = await RunAsync(store, devices,
            Write(Param("rows", "$lines"), Param("numberFormat", "$formats")),
            Read(asText: true));

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        var row = Assert.Single(Rows(store, "back"));
        Assert.Equal("2026-01-05", row[0].AsText());
        Assert.Equal("120.50", row[1].AsText());
    }

    [Fact]
    public async Task Where_the_text_sits_and_how_wide_the_column_is_reach_the_file()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "标题"), Param("alignment", "center"), Param("wrapText", "true"),
                Param("columnWidth", "30")));

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        // None of these can be seen by reading the cells back, so the file itself is asked.
        using var book = new XLWorkbook(new MemoryStream(devices.Blobs["book.xlsx"]));
        var page = book.Worksheet("Sheet1");
        Assert.Equal(XLAlignmentHorizontalValues.Center, page.Cell(1, 1).Style.Alignment.Horizontal);
        Assert.True(page.Cell(1, 1).Style.Alignment.WrapText);
        Assert.Equal(30, page.Column(1).Width);
    }

    [Fact]
    public async Task A_sheet_can_be_taken_off_the_tabs_and_put_back_on_them()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var hid = await RunAsync(store, devices,
            Write(Param("rows", "a"), Param("sheet", "A")),
            Write(Param("rows", "b"), Param("sheet", "B")),
            OnSheet("excel.setSheetVisibility", Param("sheet", "B"), Param("visibility", "hidden")));

        Assert.True(hid.Succeeded, hid.Key + " " + hid.Detail);
        using (var book = Book(devices))
        {
            Assert.Equal(XLWorksheetVisibility.Hidden, book.Worksheet("B").Visibility);
            Assert.Equal(XLWorksheetVisibility.Visible, book.Worksheet("A").Visibility);
        }

        var shown = await RunAsync(store, devices,
            OnSheet("excel.setSheetVisibility", Param("sheet", "B"), Param("visibility", "visible")));

        Assert.True(shown.Succeeded, shown.Key + " " + shown.Detail);
        using var after = Book(devices);
        Assert.Equal(XLWorksheetVisibility.Visible, after.Worksheet("B").Visibility);
    }

    [Fact]
    public async Task The_last_sheet_that_is_on_show_is_not_taken_off_the_tabs()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "a")),
            OnSheet("excel.setSheetVisibility", Param("visibility", "veryHidden")));

        // Nothing on show is a workbook nobody can unhide from Excel's own menu.
        Assert.Equal("Run.LastVisibleSheet", result.Key);
    }

    [Fact]
    public async Task A_sheet_is_copied_under_a_name_of_its_own()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "a"), Param("sheet", "一月")),
            OnSheet("excel.copySheet", Param("sheet", "一月"), Param("newName", "二月")),
            Read(sheet: "二月"));

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal("a", Assert.Single(Rows(store, "back"))[0].AsText());

        // Both sheets, the copy behind the one it came from and holding what that one holds.
        using var book = Book(devices);
        Assert.Equal(["一月", "二月"], book.Worksheets.Select(page => page.Name));
        Assert.Equal("a", book.Worksheet("一月").Cell(1, 1).GetString());
    }

    [Fact]
    public async Task A_copy_can_be_put_in_front_of_the_sheets_that_are_there()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "a"), Param("sheet", "一月")),
            OnSheet("excel.copySheet", Param("sheet", "一月"), Param("newName", "零月"),
                Param("at", "first")));

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        using var book = Book(devices);
        Assert.Equal(["零月", "一月"], book.Worksheets.Select(page => page.Name));
    }

    [Fact]
    public async Task A_sheet_is_copied_into_another_file_that_is_not_there_yet()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "a")),
            OnSheet("excel.copySheet", Param("sheet", "Sheet1"), Param("into", "other.xlsx")),
            new ExecutableStep
            {
                Type = "excel.readSheet",
                Parameters =
                [
                    Param("path", "other.xlsx"),
                    Param("sheet", "Sheet1"),
                    Param("hasHeader", "false"),
                    Param("maxRows", "0"),
                    Param("resultVariable", "moved"),
                ],
            });

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        Assert.Equal("a", store.Local.Values["moved"].Items[0].Items[0].AsText());

        // The empty name keeps the sheet's own, which is what copying a template into a new file
        // wants; the file it came from still holds the one sheet it had.
        using var target = Book(devices, "other.xlsx");
        Assert.Equal(["Sheet1"], target.Worksheets.Select(page => page.Name));
        using var source = Book(devices);
        Assert.Equal(["Sheet1"], source.Worksheets.Select(page => page.Name));
    }

    [Fact]
    public async Task A_copy_under_a_name_the_workbook_already_has_is_refused()
    {
        var store = new VariableStore();
        var devices = new FakeDeviceLayer();

        var result = await RunAsync(store, devices,
            Write(Param("rows", "a"), Param("sheet", "A")),
            Write(Param("rows", "b"), Param("sheet", "B")),
            OnSheet("excel.copySheet", Param("sheet", "A"), Param("newName", "B")));

        Assert.Equal("Run.SheetNameTaken", result.Key);
    }
}
