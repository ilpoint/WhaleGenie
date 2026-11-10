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

// JSON and CSV: reading a path or a row, and writing one back.
public partial class DeviceActionTests
{
    [Fact]
    public async Task Reading_json_walks_a_dotted_path()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["config.json"] = """{"server":{"name":"alpha","port":8080}}""";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readJson", Param("path", "config.json"), Param("query", "server.name"),
                Returns("value=host")),
            Step("file.readJson", Param("path", "config.json"), Param("query", "server.port"),
                Returns("value=port")),
        ], devices);

        Assert.Equal("alpha", store.Local.Values["host"].AsText());
        Assert.Equal(8080d, store.Local.Values["port"].Number);
    }

    [Fact]
    public async Task Writing_json_keeps_the_rest_and_makes_missing_objects()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["config.json"] = """{"server":{"name":"alpha"}}""";
        await RunAsync(
        [
            Step("file.writeJson", Param("path", "config.json"), Param("query", "server.port"),
                Param("writeValue", "9000")),
        ], devices);

        var written = devices.Files.Store["config.json"];
        Assert.Contains("\"name\": \"alpha\"", written);
        Assert.Contains("\"port\": 9000", written);
    }

    [Fact]
    public async Task Reading_csv_splits_rows_and_skips_a_header()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["rows.csv"] = "name,age\r\nalice,30\r\nbob,41";
        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "true"), Returns("rows=rows")),
        ], devices);

        var rows = store.Local.Values["rows"];
        Assert.Equal(2, rows.Items.Count);
        Assert.Equal("alice", rows.Items[0].Field("name")!.AsText());
        Assert.Equal("41", rows.Items[1].Field("age")!.AsText());
    }

    [Fact]
    public async Task One_value_of_a_csv_can_be_changed_and_written_back()
    {
        // What "change one cell of a CSV" comes to now that there are cells to point at: read the
        // column, change the item at that position, write the column back.
        var devices = new FakeDeviceLayer();
        devices.Files.Store["names.csv"] = "alice\r\nbob\r\ncarol";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "names.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("shape", "values"), Returns("rows=names")),
            Step("control.listSet", Param("name", "names"), Param("listIndex", "1"), Param("value", "Betty")),
            Step("file.writeCsv", Param("path", "out.csv"), Param("rows", "$names"),
                Param("separator", "comma")),
        ], devices);

        Assert.Equal("alice\nBetty\ncarol", devices.Files.Store["out.csv"].Replace("\r\n", "\n").TrimEnd('\n'));
    }

    [Fact]
    public async Task Writing_csv_quotes_the_cells_that_need_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["in.csv"] = "a,\"b,c\"";
        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=rows")),
            Step("file.writeCsv", Param("path", "out.csv"), Param("rows", "$rows"),
                Param("separator", "comma")),
        ], devices);

        // The cell holding the separator comes back out quoted, and the file ends its last line
        // the way every writer ends one: with a line terminator, rather than a line left hanging.
        Assert.Equal("a,\"b,c\"\r\n", devices.Files.Store["out.csv"]);
    }

    /// <summary>
    /// A row is a record, so a cell is read by the name above its column rather than by counting
    /// along the line: the counting is what goes wrong when somebody else's file grows a column.
    /// </summary>
    [Fact]
    public async Task A_row_read_with_a_header_is_read_by_column_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["rows.csv"] = "name,age\r\nalice,30";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "true"), Returns("rows=rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal(["name", "age"], row.Fields.Select(field => field.Name));
        Assert.Equal("alice", row.Field("name")!.AsText());
        Assert.Equal("30", row.Field("age")!.AsText());

        // The row the names were written on is still left out of the data, which is what the header
        // setting promises.
        Assert.Single(store.Local.Values["rows"].Items);
    }

    /// <summary>
    /// A file whose first row is not a header still names its columns, with the letters somebody
    /// reading the file would use — the same names the "only these columns" field takes.
    /// </summary>
    [Fact]
    public async Task A_row_read_without_a_header_names_its_columns_by_letter()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["plain.csv"] = "alice,30";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "plain.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal(["A", "B"], row.Fields.Select(field => field.Name));
        Assert.Equal("alice", row.Field("A")!.AsText());
        Assert.Equal("30", row.Field("B")!.AsText());
    }

    [Fact]
    public async Task Reading_csv_can_start_further_down_the_file_and_stop_early()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["big.csv"] = "a,1\r\nb,2\r\nc,3\r\nd,4";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "big.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("startRow", "2"), Param("maxRows", "2"),
                Returns("rows=rows")),
        ], devices);

        var rows = store.Local.Values["rows"].Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal("b", rows[0].Field("A")!.AsText());
        Assert.Equal("c", rows[1].Field("A")!.AsText());
    }

    [Fact]
    public async Task Reading_csv_leaves_out_the_blank_lines_a_file_ends_with()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["rows.csv"] = "a,b\r\n\r\nc,d\r\n\r\n";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=kept")),
            Step("file.readCsv", Param("path", "rows.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("skipBlankLines", "false"),
                Returns("rows=every")),
        ], devices);

        Assert.Equal(2, store.Local.Values["kept"].Items.Count);
        Assert.Equal(4, store.Local.Values["every"].Items.Count);
    }

    [Fact]
    public async Task Reading_csv_can_use_a_character_the_picker_does_not_offer()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["odd.csv"] = "a#b#c";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "odd.csv"), Param("separator", "comma"),
                Param("separatorText", "#"), Param("hasHeader", "false"),
                Returns("rows=rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal(3, row.Fields.Count);
        Assert.Equal("c", row.Field("C")!.AsText());
    }

    [Fact]
    public async Task Reading_csv_can_trim_the_spaces_around_a_cell()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["padded.csv"] = "a , b";

        var (_, _, store) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "padded.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Param("trimCells", "true"), Returns("rows=rows")),
        ], devices);

        var row = Assert.Single(store.Local.Values["rows"].Items);
        Assert.Equal("a", row.Field("A")!.AsText());
        Assert.Equal("b", row.Field("B")!.AsText());
    }

    /// <summary>
    /// A log is the reason adding to a CSV matters: the first run makes the file and its header,
    /// and every run after it adds rows under the same header.
    /// </summary>
    [Fact]
    public async Task Adding_to_a_csv_writes_the_header_once_and_the_rows_after_it()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["first.csv"] = "alice,30";
        devices.Files.Store["second.csv"] = "bob,41";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "first.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=first")),
            Step("file.readCsv", Param("path", "second.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=second")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$first"),
                Param("header", "name,age"), Param("separator", "comma"),
                Param("writeMode", "append")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$second"),
                Param("header", "name,age"), Param("separator", "comma"),
                Param("writeMode", "append")),
        ], devices);

        Assert.Equal("name,age\r\nalice,30\r\nbob,41\r\n", devices.Files.Store["log.csv"]);
    }

    /// <summary>
    /// A cell is a cell: text that begins with a character a spreadsheet would read as a formula
    /// is written exactly as the macro held it. Escaping those changes the bytes of a file the step
    /// was asked to write, and the file may well be going to something that is not a spreadsheet.
    /// </summary>
    [Fact]
    public async Task A_csv_cell_that_looks_like_a_formula_is_written_as_it_stands()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["in.csv"] = "=SUM(A1),@mine,-1";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=rows")),
            Step("file.writeCsv", Param("path", "out.csv"), Param("rows", "$rows"),
                Param("separator", "comma")),
        ], devices);

        Assert.Equal("=SUM(A1),@mine,-1\r\n", devices.Files.Store["out.csv"]);
    }

    [Fact]
    public async Task Writing_csv_can_end_its_lines_and_quote_every_cell_the_way_it_was_told()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["in.csv"] = "a,b\r\nc,d";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=rows")),
            Step("file.writeCsv", Param("path", "unix.csv"), Param("rows", "$rows"),
                Param("separator", "comma"), Param("lineEnding", "unix"),
                Param("quoteAll", "true")),
        ], devices);

        Assert.Equal("\"a\",\"b\"\n\"c\",\"d\"\n", devices.Files.Store["unix.csv"]);
    }

    [Fact]
    public async Task An_empty_csv_cell_is_left_empty_unless_it_is_asked_to_be_shown()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["in.csv"] = "a,,c";

        await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=rows")),
            Step("file.writeCsv", Param("path", "bare.csv"), Param("rows", "$rows"),
                Param("separator", "comma")),
            Step("file.writeCsv", Param("path", "shown.csv"), Param("rows", "$rows"),
                Param("separator", "comma"), Param("emptyCells", "quoted")),
            Step("file.readCsv", Param("path", "shown.csv"), Param("separator", "comma"),
                Param("hasHeader", "false"), Returns("rows=back")),
        ], devices);

        // Both are a cell holding nothing; the second one is for the reader that wants to see a
        // cell there, and it reads back as the same empty cell.
        Assert.Equal("a,,c\r\n", devices.Files.Store["bare.csv"]);
        Assert.Equal("a,\"\",c\r\n", devices.Files.Store["shown.csv"]);
    }

    /// <summary>
    /// A CSV somebody else's program keeps gets columns of its own, and adding to it has to follow
    /// the names in its first line rather than counts of cells.
    /// </summary>
    [Fact]
    public async Task Adding_to_a_csv_by_name_puts_each_cell_under_the_column_that_has_its_name()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["log.csv"] = "日期,订单号,金额\r\n2026-01-05,A123,120\r\n";
        devices.Files.Store["in.csv"] = "订单号,金额\r\nB456,80";

        await RunAsync(
        [
            // A row carries the names above its columns, so the step does not have to write those
            // names out again for the file to be added to by name.
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "true"), Returns("rows=rows")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$rows"),
                Param("separator", "comma"), Param("writeMode", "append"), Param("align", "true")),
        ], devices);

        // The date column is the file's own and this step knows nothing about it: it stays empty
        // in the new line rather than taking the order number.
        Assert.Equal("日期,订单号,金额\r\n2026-01-05,A123,120\r\n,B456,80\r\n",
            devices.Files.Store["log.csv"]);
    }

    [Fact]
    public async Task A_csv_column_name_the_file_has_not_got_is_reported()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["log.csv"] = "日期,订单号\r\n2026-01-05,A123\r\n";
        devices.Files.Store["in.csv"] = "总价\r\n1";

        var (result, _, _) = await RunAsync(
        [
            Step("file.readCsv", Param("path", "in.csv"), Param("separator", "comma"),
                Param("hasHeader", "true"), Returns("rows=rows")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$rows"),
                Param("separator", "comma"), Param("writeMode", "append"), Param("align", "true")),
        ], devices);

        Assert.Equal("Run.NoSuchColumn", result.Key);
    }

    [Fact]
    public async Task A_csv_cell_added_by_name_needs_a_name_of_its_own()
    {
        var devices = new FakeDeviceLayer();
        devices.Files.Store["log.csv"] = "日期,订单号\r\n2026-01-05,A123\r\n";

        // A row of plain cells rather than a record: nothing says what its two cells are called, and
        // the step names one column for them. Half a name is worse than no name — the other cell
        // would land in whatever column happens to be second.
        var (result, _, _) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "rows"), Param("value", "[[\"A1\", 2]]")),
            Step("file.writeCsv", Param("path", "log.csv"), Param("rows", "$rows"),
                Param("header", "订单号"), Param("separator", "comma"),
                Param("writeMode", "append"), Param("align", "true")),
        ], devices);

        Assert.Equal("Run.AlignNeedsNames", result.Key);
    }

    [Fact]
    public async Task Saving_and_loading_variables_round_trips_them()
    {
        var devices = new FakeDeviceLayer();
        var (_, _, store) = await RunAsync(
        [
            Step("control.setVariable", Param("name", "counter"), Param("value", "5")),
            Step("file.saveVariables", Param("path", "state.json")),
            Step("control.setVariable", Param("name", "counter"), Param("value", "0")),
            Step("file.loadVariables", Param("path", "state.json")),
        ], devices);

        Assert.Equal("5", store.Local.Values["counter"].AsText());
        Assert.Contains("\"counter\": 5", devices.Files.Store["state.json"]);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_stops_the_step_with_a_reason()
    {
        var devices = new FakeDeviceLayer
        {
            Refusal = new DeviceActionException("Run.FileNotFound", "ghost.txt"),
        };
        var (result, _, _) = await RunAsync(
            [Step("file.readText", Param("path", "ghost.txt"))], devices);

        Assert.False(result.Succeeded);
        Assert.Equal("Run.FileNotFound", result.Key);
        Assert.Equal("ghost.txt", result.Detail);
    }
}
