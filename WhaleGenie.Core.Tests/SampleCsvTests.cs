using System.Threading.Tasks;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// CSV files as they come off somebody else's machine, rather than as this project would write
/// them. See <see cref="SampleFiles"/> for how they are run.
/// </summary>
public class SampleCsvTests
{
    [RealSampleFact("sample-1.csv", "sample-2.csv")]
    public async Task A_list_separated_by_semicolons_reads_as_a_table()
    {
        // Nothing in a text file says which character separates its cells, and this one was written
        // by a program that picked the semicolon — which is what half of Europe gets by default,
        // and what a Chinese Excel writes when the region settings say so.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("file.readCsv", SampleFiles.Param("path", "sample-1.csv"),
                SampleFiles.Param("separator", "auto"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Returns("rows=rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var rows = run.Rows("rows");
        Assert.Equal(5, rows.Count);

        // Every row carries the names the file wrote above its columns, in the file's own order.
        var first = rows[0];
        Assert.Equal(6, first.Fields.Count);
        Assert.Equal("Task Name", first.Fields[1].Name);
        Assert.Equal("Research Topic", first.Fields[1].Value.AsText());
        Assert.Equal("In Progress", first.Fields[3].Value.AsText());

        // And a cell is read by that name rather than by counting along the line.
        Assert.Equal("Research Topic", first.Cell("Task Name").AsText());
    }

    [RealSampleFact("sample-1.csv")]
    public async Task Naming_the_separator_still_reads_the_file_that_way()
    {
        // Working it out from the file is a convenience, not an override: a macro that says which
        // character it is reading has to get that, or a file with one odd line in it would be read
        // a different way on the day that line changed.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("file.readCsv", SampleFiles.Param("path", "sample-1.csv"),
                SampleFiles.Param("separator", "semicolon"),
                SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Returns("rows=rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var rows = run.Rows("rows");
        Assert.Equal(6, rows.Count);
        Assert.Equal(6, rows[0].Fields.Count);
        Assert.Equal("Task ID", rows[0].Cell("A").AsText());
    }

    [RealSampleFact("sample-2.csv")]
    public async Task Cells_that_hold_nothing_are_still_cells()
    {
        // A row with a gap in it is not a row with fewer columns: everything after the gap has to
        // stay where the person reading the file sees it.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("file.readCsv", SampleFiles.Param("path", "sample-2.csv"),
                SampleFiles.Param("separator", "auto"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Returns("rows=rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var rows = run.Rows("rows");
        Assert.Equal(3, rows.Count);
        Assert.Equal(10, rows[0].Fields.Count);

        var last = rows[2];
        Assert.Equal("The best image ever", last.Fields[0].Value.AsText());
        Assert.Equal(string.Empty, last.Fields[4].Value.AsText());
        Assert.Equal(string.Empty, last.Fields[5].Value.AsText());
        Assert.Equal("Mary Maryson", last.Fields[6].Value.AsText());
        Assert.Equal("dontknow", last.Fields[9].Value.AsText());

        // And the text in a cell is nobody's business but the file's: a cell holding semicolons
        // stays one cell, because the commas are what separates these.
        Assert.Equal("Politics; Memes; Cats", last.Fields[3].Value.AsText());
    }

    [RealSampleFact("sample-2.csv")]
    public async Task A_table_that_was_written_comes_back_the_way_it_went_in()
    {
        // The everyday round trip: read a file, put the rows somewhere else, and read that back. A
        // cell with a comma or a quote in it is where a home-made writer and a home-made reader
        // both quietly lose a column, so the check is that the table is unchanged cell for cell.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("file.readCsv", SampleFiles.Param("path", "sample-2.csv"),
                SampleFiles.Param("separator", "auto"), SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Returns("rows=rows")),
            SampleFiles.Step("file.writeCsv", SampleFiles.Param("path", "back.csv"),
                SampleFiles.Param("rows", "$rows"), SampleFiles.Param("separator", "comma"),
                SampleFiles.Param("mode", "replace")),
            SampleFiles.Step("file.readCsv", SampleFiles.Param("path", "back.csv"),
                SampleFiles.Param("separator", "auto"), SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Returns("rows=again")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);
        Assert.True(run.Devices.Files.ContainsKey("back.csv"), "the file should have been written");

        var rows = run.Rows("rows");
        var again = run.Rows("again");
        Assert.Equal(rows.Count, again.Count);
        for (var row = 0; row < rows.Count; row++)
        {
            // The names come back with the cells: a row written out and read back is the same row,
            // column name for column name.
            Assert.Equal(
                rows[row].Fields.Select(field => field.Name),
                again[row].Fields.Select(field => field.Name));
            Assert.Equal(
                rows[row].Fields.Select(field => field.Value.AsText()),
                again[row].Fields.Select(field => field.Value.AsText()));
        }
    }
}
