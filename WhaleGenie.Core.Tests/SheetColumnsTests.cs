using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Reading a table by its columns rather than by counting them. The order of the columns in a
/// spreadsheet somebody else maintains is not something a macro can rely on, so a step names the
/// column it wants and the reading follows it.
/// </summary>
public class SheetColumnsTests
{
    private static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>
    /// A workbook holding one small table, written with the engine's own writer: the checks below
    /// are about reading it back, and a file written here is a file whose shape is known.
    /// </summary>
    private static async Task<(VariableStore Store, FakeDeviceLayer Devices)> BookAsync()
    {
        var devices = new FakeDeviceLayer();
        var store = new VariableStore();
        store.Local.Set("table", Value.FromList(
        [
            Value.FromList([Value.FromText("订单号"), Value.FromText("金额"), Value.FromText("日期")]),
            Value.FromList([Value.FromText("A123"), Value.FromNumber(120), Value.FromText("2026-01-05")]),
            Value.FromList([Value.FromText("B456"), Value.FromNumber(80), Value.FromText("2026-01-06")]),
            Value.FromList([Value.FromText("A789"), Value.FromNumber(200), Value.FromText("2026-01-07")]),
        ]));

        await WriteAsync(store, devices, "$table");
        return (store, devices);
    }

    /// <summary>Writes a table into the fake machine's workbook, which is where the reading reads it from.</summary>
    private static async Task WriteAsync(VariableStore store, FakeDeviceLayer devices, string rows)
    {
        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(
        [
            new ExecutableStep
            {
                Type = "excel.writeSheet",
                Parameters =
                [
                    Param("path", "book.xlsx"),
                    Param("sheet", "Sheet1"),
                    Param("rows", rows),
                    Param("mode", "replace"),
                    Param("formula", "false"),
                ],
            },
        ]);

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
    }

    private static async Task<VariableStore> ReadAsync(VariableStore store, FakeDeviceLayer devices,
        params ExecutableParameter[] parameters)
    {
        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(
        [
            new ExecutableStep
            {
                Type = "excel.readSheet",
                Parameters =
                [
                    Param("path", "book.xlsx"),
                    Param("sheet", "Sheet1"),
                    Param("hasHeader", "true"),
                    Param("maxRows", "0"),
                    .. parameters,
                ],
            },
        ]);

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);
        return store;
    }

    private static IReadOnlyList<IReadOnlyList<Value>> Rows(VariableStore store, string name)
        => [.. store.Local.Values[name].Items.Select(row => (IReadOnlyList<Value>)row.Items)];

    private static IReadOnlyList<string> Texts(VariableStore store, string name)
        => [.. store.Local.Values[name].Items.Select(item => item.AsText())];

    [Fact]
    public async Task Columns_are_named_the_way_the_header_names_them()
    {
        var (store, devices) = await BookAsync();

        await ReadAsync(store, devices,
            Param("columns", "金额"), Param("headerVariable", "names"),
            Param("resultVariable", "amounts"));

        Assert.Equal(["金额"], Texts(store, "names"));
        Assert.Equal(3, Rows(store, "amounts").Count);
        Assert.Equal(120, Rows(store, "amounts")[0][0].AsNumber());
        Assert.Equal(200, Rows(store, "amounts")[2][0].AsNumber());
    }

    [Fact]
    public async Task A_column_can_be_named_by_the_letter_it_sits_under()
    {
        var (store, devices) = await BookAsync();

        await ReadAsync(store, devices, Param("columns", "B, A"), Param("resultVariable", "picked"));

        // In the order they were written, not the order they sit in.
        Assert.Equal("A123", Rows(store, "picked")[0][1].AsText());
        Assert.Equal(120, Rows(store, "picked")[0][0].AsNumber());
    }

    [Fact]
    public async Task A_column_the_table_does_not_have_is_a_mistake_and_is_reported()
    {
        var (store, devices) = await BookAsync();

        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(
        [
            new ExecutableStep
            {
                Type = "excel.readSheet",
                Parameters = [Param("path", "book.xlsx"), Param("columns", "总价")],
            },
        ]);

        Assert.Equal("Run.NoSuchColumn", result.Key);
    }

    [Fact]
    public async Task One_column_can_come_back_as_a_plain_list_of_values()
    {
        var (store, devices) = await BookAsync();

        await ReadAsync(store, devices,
            Param("columns", "金额"), Param("shape", "values"), Param("resultVariable", "amounts"));

        Assert.Equal([120d, 80d, 200d],
            store.Local.Values["amounts"].Items.Select(item => item.AsNumber()));
    }

    [Fact]
    public async Task Flattening_more_than_one_column_says_so_rather_than_picking_one()
    {
        var (store, devices) = await BookAsync();

        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(
        [
            new ExecutableStep
            {
                Type = "excel.readSheet",
                Parameters =
                [
                    Param("path", "book.xlsx"),
                    Param("columns", "订单号, 金额"),
                    Param("shape", "values"),
                ],
            },
        ]);

        Assert.Equal("Run.OneColumnOnly", result.Key);
    }

    [Fact]
    public async Task Only_the_rows_that_match_are_read()
    {
        var (store, devices) = await BookAsync();

        await ReadAsync(store, devices,
            Param("matchColumn", "订单号"), Param("matchValue", "A123"), Param("resultVariable", "found"));

        Assert.Single(Rows(store, "found"));
        Assert.Equal("A123", Rows(store, "found")[0][0].AsText());
    }

    [Fact]
    public async Task A_match_can_be_part_of_the_cell_and_the_column_kept_need_not_be_the_one_matched()
    {
        var (store, devices) = await BookAsync();

        await ReadAsync(store, devices,
            Param("matchColumn", "订单号"), Param("matchValue", "A"), Param("matchMode", "startsWith"),
            Param("columns", "金额"), Param("shape", "values"), Param("resultVariable", "amounts"));

        Assert.Equal([120d, 200d],
            store.Local.Values["amounts"].Items.Select(item => item.AsNumber()));
    }

    [Fact]
    public async Task A_table_under_a_title_reads_from_the_row_its_header_is_on()
    {
        var devices = new FakeDeviceLayer();
        var store = new VariableStore();
        store.Local.Set("table", Value.FromList(
        [
            Value.FromList([Value.FromText("2026 年 1 月 销售明细")]),
            Value.FromList([]),
            Value.FromList([Value.FromText("订单号"), Value.FromText("金额")]),
            Value.FromList([Value.FromText("A123"), Value.FromNumber(120)]),
        ]));
        await WriteAsync(store, devices, "$table");

        await ReadAsync(store, devices,
            Param("headerRow", "3"), Param("headerVariable", "names"), Param("resultVariable", "rows"));

        Assert.Equal(["订单号", "金额"], Texts(store, "names"));
        Assert.Single(Rows(store, "rows"));
        Assert.Equal("A123", Rows(store, "rows")[0][0].AsText());
    }

    [Fact]
    public async Task A_csv_reads_the_same_way_the_sheet_does()
    {
        var devices = new FakeDeviceLayer();
        devices.Files["rows.csv"] =
            "订单号,金额,日期\r\nA123,120,2026-01-05\r\nB456,80,2026-01-06\r\n";
        var store = new VariableStore();

        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(
        [
            new ExecutableStep
            {
                Type = "file.readCsv",
                Parameters =
                [
                    Param("path", "rows.csv"),
                    Param("separator", "comma"),
                    Param("hasHeader", "true"),
                    Param("skipBlankLines", "true"),
                    Param("startRow", "1"),
                    Param("maxRows", "0"),
                    Param("trim", "false"),
                    Param("columns", "金额"),
                    Param("shape", "values"),
                    Param("resultVariable", "amounts"),
                ],
            },
        ]);

        Assert.True(result.Succeeded, result.Key + " " + result.Detail);

        // A CSV cell is text: the file says nothing about whether 120 is a number, so it stays what
        // it was written as; a macro that wants a number says so with number($amounts[0]).
        Assert.Equal(["120", "80"],
            store.Local.Values["amounts"].Items.Select(item => item.AsText()));
    }
}
