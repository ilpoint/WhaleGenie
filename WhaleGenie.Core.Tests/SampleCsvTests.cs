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
                SampleFiles.Param("headerVariable", "columns"),
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var columns = run.Texts("columns");
        Assert.Equal(6, columns.Count);
        Assert.Equal("Task Name", columns[1]);

        var rows = run.Rows("rows");
        Assert.Equal(5, rows.Count);
        Assert.Equal(6, rows[0].Count);
        Assert.Equal("Research Topic", rows[0][1].AsText());
        Assert.Equal("In Progress", rows[0][3].AsText());
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
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var rows = run.Rows("rows");
        Assert.Equal(6, rows.Count);
        Assert.Equal(6, rows[0].Count);
        Assert.Equal("Task ID", rows[0][0].AsText());
    }

    [RealSampleFact("sample-2.csv")]
    public async Task Cells_that_hold_nothing_are_still_cells()
    {
        // A row with a gap in it is not a row with fewer columns: everything after the gap has to
        // stay where the person reading the file sees it.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("file.readCsv", SampleFiles.Param("path", "sample-2.csv"),
                SampleFiles.Param("separator", "auto"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var rows = run.Rows("rows");
        Assert.Equal(3, rows.Count);
        Assert.Equal(10, rows[0].Count);

        var last = rows[2];
        Assert.Equal("The best image ever", last[0].AsText());
        Assert.Equal(string.Empty, last[4].AsText());
        Assert.Equal(string.Empty, last[5].AsText());
        Assert.Equal("Mary Maryson", last[6].AsText());
        Assert.Equal("dontknow", last[9].AsText());

        // And the text in a cell is nobody's business but the file's: a cell holding semicolons
        // stays one cell, because the commas are what separates these.
        Assert.Equal("Politics; Memes; Cats", last[3].AsText());
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
                SampleFiles.Param("resultVariable", "rows")),
            SampleFiles.Step("file.writeCsv", SampleFiles.Param("path", "back.csv"),
                SampleFiles.Param("rows", "$rows"), SampleFiles.Param("separator", "comma"),
                SampleFiles.Param("mode", "replace")),
            SampleFiles.Step("file.readCsv", SampleFiles.Param("path", "back.csv"),
                SampleFiles.Param("separator", "auto"), SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Param("resultVariable", "again")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);
        Assert.True(run.Devices.Files.ContainsKey("back.csv"), "the file should have been written");

        var rows = run.Rows("rows");
        var again = run.Rows("again");
        Assert.Equal(rows.Count, again.Count);
        for (var row = 0; row < rows.Count; row++)
        {
            Assert.Equal(rows[row].Count, again[row].Count);
            for (var cell = 0; cell < rows[row].Count; cell++)
            {
                Assert.Equal(rows[row][cell].AsText(), again[row][cell].AsText());
            }
        }
    }
}
