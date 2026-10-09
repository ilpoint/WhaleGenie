using System.Linq;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Workbooks as they come off somebody else's machine: a 390-row export with real numbers in it, a
/// small one with gaps in the middle of a row, and one in the format Excel wrote before 2007. See
/// <see cref="SampleFiles"/> for how they are run.
/// </summary>
public class SampleSheetTests
{
    [RealSampleFact("sample1.xlsx")]
    public async Task A_whole_exported_table_reads_with_the_kind_of_thing_in_each_column()
    {
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample1.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Param("headerVariable", "columns"),
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        Assert.Equal(["Postcode", "Sales_Rep_ID", "Sales_Rep_Name", "Year", "Value"],
            run.Texts("columns"));

        // Three hundred and ninety rows of sales: whoever reads this is going to add the values up
        // and compare the years, so a number has to arrive as a number.
        var rows = run.Rows("rows");
        Assert.Equal(390, rows.Count);
        Assert.Equal("2121", rows[0][0].AsText());
        Assert.Equal(456, rows[0][1].AsNumber());
        Assert.Equal("Jane", rows[0][2].AsText());
        Assert.Equal(2011, rows[0][3].AsNumber());
        Assert.True(rows[0][4].AsNumber() > 84000, "the value column should be a number to add up");
    }

    [RealSampleFact("sample1.xlsx")]
    public async Task Part_of_an_exported_table_can_be_read_on_its_own()
    {
        // The other half of an export is a worksheet somebody laid out themselves, and what is
        // wanted from it is one block of cells rather than the whole used range.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample1.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("range", "C2:E4"),
                SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var rows = run.Rows("rows");
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows[0].Count);
        Assert.Equal("Jane", rows[0][0].AsText());
        Assert.Equal(2011, rows[0][1].AsNumber());
    }

    [RealSampleFact("sample2.xlsx")]
    public async Task A_gap_in_the_middle_of_a_row_keeps_the_columns_after_it_in_place()
    {
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample2.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Param("headerVariable", "columns"),
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);
        Assert.Equal(10, run.Texts("columns").Count);

        var rows = run.Rows("rows");
        Assert.Equal(3, rows.Count);

        // The third record has nothing in two of its middle columns, which is how a real file
        // looks: the columns after the gap still line up with their names.
        var last = rows[2];
        Assert.Equal(10, last.Count);
        Assert.Equal(string.Empty, last[4].AsText());
        Assert.Equal(string.Empty, last[5].AsText());
        Assert.Equal("Mary Maryson", last[6].AsText());
        Assert.Equal("2015.4", last[8].AsText());
    }

    /// <summary>
    /// The old binary format, which is what an export from an older Office looks like. It is not
    /// something the reader for .xlsx can open at all, so without a reader of its own the file is
    /// simply a file this program refuses — and "my spreadsheet is not a spreadsheet" is the worst
    /// kind of answer for somebody who just wants to add up a column.
    /// </summary>
    [RealSampleFact("sample1.xls")]
    public async Task A_workbook_in_the_old_format_is_read_too()
    {
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.listSheets", SampleFiles.Param("path", "sample1.xls"),
                SampleFiles.Param("resultVariable", "sheets")),
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample1.xls"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Param("headerVariable", "columns"),
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        Assert.Equal(["Sheet1"], run.Texts("sheets"));

        // The file's own first column has no name above it — the row above the names holds that
        // column's first value instead, which is what a real sheet laid out by hand looks like.
        var columns = run.Texts("columns");
        Assert.Equal("First Name", columns[1]);
        Assert.Equal("Id", columns[7]);

        var rows = run.Rows("rows");
        Assert.Equal(50, rows.Count);
        Assert.Equal("Dulce", rows[0][1].AsText());
        Assert.Equal("United States", rows[0][4].AsText());
        Assert.Equal(32, rows[0][5].AsNumber());
    }

    [RealSampleFact("sample1.xls")]
    public async Task Part_of_a_workbook_in_the_old_format_can_be_read_on_its_own()
    {
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample1.xls"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("range", "C2:E3"),
                SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Param("resultVariable", "rows")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        var rows = run.Rows("rows");
        Assert.Equal(2, rows.Count);
        Assert.Equal(3, rows[0].Count);
        Assert.Equal("Abril", rows[0][0].AsText());
        Assert.Equal("Female", rows[0][1].AsText());
        Assert.Equal("United States", rows[0][2].AsText());
    }

    [RealSampleFact("sample1.xls")]
    public async Task A_workbook_in_the_old_format_says_so_when_a_step_tries_to_change_it()
    {
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.addSheet", SampleFiles.Param("path", "sample1.xls"),
                SampleFiles.Param("sheet", "Extra")));

        // Not "this is not a workbook" — it is one, and it is one this program can read. What it
        // cannot do is write it, and the answer says which file to make instead.
        Assert.Equal("Run.OldFormatReadOnly", run.Result.Key);
    }

    [RealSampleFact("sample1.xlsx")]
    public async Task A_table_read_out_of_a_sheet_goes_into_another_sheet_of_the_same_book()
    {
        // What a macro does with a table is rarely only read it: it picks some of the rows and puts
        // them somewhere they can be looked at. The sheet that was read from must come out of that
        // untouched, and the numbers must still be numbers once they land.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample1.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("range", "A1:E4"),
                SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Param("headerVariable", "columns"),
                SampleFiles.Param("resultVariable", "rows")),
            SampleFiles.Step("excel.writeSheet", SampleFiles.Param("path", "sample1.xlsx"),
                SampleFiles.Param("sheet", "Top"), SampleFiles.Param("rows", "$rows"),
                SampleFiles.Param("header", "$columns"), SampleFiles.Param("mode", "replace")),
            SampleFiles.Step("excel.listSheets", SampleFiles.Param("path", "sample1.xlsx"),
                SampleFiles.Param("resultVariable", "sheets")),
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample1.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Param("resultVariable", "still")),
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample1.xlsx"),
                SampleFiles.Param("sheet", "Top"), SampleFiles.Param("hasHeader", "true"),
                SampleFiles.Param("headerVariable", "topColumns"),
                SampleFiles.Param("resultVariable", "top")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);

        Assert.Equal(["Sheet1", "Top"], run.Texts("sheets"));
        Assert.Equal(390, run.Rows("still").Count);

        Assert.Equal(["Postcode", "Sales_Rep_ID", "Sales_Rep_Name", "Year", "Value"],
            run.Texts("topColumns"));

        var top = run.Rows("top");
        Assert.Equal(3, top.Count);
        Assert.Equal("2121", top[0][0].AsText());
        Assert.True(top[0][3].IsNumber, "the year should still be a number after being written");
        Assert.Equal(2011, top[0][3].AsNumber());
        Assert.Equal(84219.4973106866, top[0][4].AsNumber(), 6);
    }

    [RealSampleFact("sample2.xlsx")]
    public async Task A_workbook_written_by_this_program_can_be_read_back_whole()
    {
        // Read a file, write everything back out to a new one, and read that: a sheet that cannot
        // survive that is a sheet a macro cannot copy. The written file is a real .xlsx on the
        // stand-in disk, so this is the writer and the reader answering to each other.
        var run = await SampleFiles.RunAsync(
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "sample2.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Param("resultVariable", "rows")),
            SampleFiles.Step("excel.writeSheet", SampleFiles.Param("path", "copy.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("rows", "$rows"),
                SampleFiles.Param("mode", "replace")),
            SampleFiles.Step("excel.readSheet", SampleFiles.Param("path", "copy.xlsx"),
                SampleFiles.Param("sheet", "Sheet1"), SampleFiles.Param("hasHeader", "false"),
                SampleFiles.Param("resultVariable", "again")));

        Assert.True(run.Result.Succeeded, run.Result.Detail);
        Assert.True(run.Devices.Blobs.ContainsKey("copy.xlsx"), "the workbook should be on disk");

        var rows = run.Rows("rows");
        var again = run.Rows("again");
        Assert.Equal(rows.Count, again.Count);
        for (var row = 0; row < rows.Count; row++)
        {
            Assert.Equal(rows[row].Count, again[row].Count);
            for (var cell = 0; cell < rows[row].Count; cell++)
            {
                Assert.Equal(rows[row][cell].AsText(), again[row][cell].AsText());
                Assert.Equal(rows[row][cell].Kind, again[row][cell].Kind);
            }
        }

        // The column of numbers in the sample is what tells a written cell that is a number from
        // one that is only shaped like one.
        Assert.Contains(again.SelectMany(row => row), cell => cell.IsNumber);
    }
}
