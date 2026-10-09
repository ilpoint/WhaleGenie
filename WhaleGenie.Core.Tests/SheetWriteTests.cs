using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Writing a table by its column names rather than by counting columns, and writing the small
/// pieces a sheet is made of — one cell, one column of plain values — without building a table
/// around them first. Reading is the other half of this and lives in <c>SheetColumnsTests</c>.
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

    /// <summary>Reads the whole sheet back as it sits, names and all, so cells can be counted.</summary>
    private static ExecutableStep Read() => new()
    {
        Type = "excel.readSheet",
        Parameters =
        [
            Param("path", "book.xlsx"),
            Param("sheet", "Sheet1"),
            Param("hasHeader", "false"),
            Param("maxRows", "0"),
            Param("resultVariable", "back"),
        ],
    };

    private static async Task<RunResult> RunAsync(VariableStore store, FakeDeviceLayer devices,
        params ExecutableStep[] steps)
        => await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(steps);

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
}
