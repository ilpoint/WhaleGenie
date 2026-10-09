using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// A workbook read and written as a file. An .xlsx is a zip of XML parts, and doing the reading
/// and writing here rather than by driving Excel is what lets a macro touch a workbook on a
/// machine with no Office on it at all, without a window opening that somebody has to close.
/// </summary>
/// <remarks>
/// ClosedXML does the file format — the zip, the XML, the shared string table, the number formats,
/// the results of formulas, the 1900 and 1904 date systems — so what is left here is only the part
/// that means something to a macro author: which sheet, which cells, and what kind of thing each
/// cell holds. That last part is the reason not to do it by hand: every one of those is a way for
/// a sheet to hand back the wrong thing, and a macro quietly working on the wrong numbers is worse
/// than one that stops.
///
/// A cell comes back as the engine's own kind of value rather than as text, because a sheet's cells
/// have kinds: a column of numbers read back as text cannot be added up.
/// </remarks>
public static class Spreadsheet
{
    /// <summary>
    /// The used cells of one sheet, as rows of values. A row is as wide as its last cell that holds
    /// something, and a row that holds nothing is still a row, so a gap in the middle of a sheet
    /// stays a gap and everything below it stays where it was.
    /// </summary>
    /// <param name="book">The bytes of the workbook, as it sits on disk.</param>
    /// <param name="sheet">
    /// Which sheet to read. An empty name means the first one, which is the one a person looking
    /// at the file sees first.
    /// </param>
    /// <param name="range">
    /// Which cells to read, as a person writes them in the name box: <c>B2:D40</c>, or <c>B2</c> for
    /// everything from that cell down and to the right. Nothing means the sheet's used cells, and
    /// those are read a row at a time as wide as the row really is; a range that was written out is
    /// read as wide as it was asked for, because that is what was asked for.
    /// </param>
    /// <param name="asText">
    /// Read what each cell shows rather than what it holds. A phone number, a date or a column of
    /// money is often stored one way and shown another, and this is the setting for a macro that
    /// wants the thing the person looking at the sheet sees.
    /// </param>
    public static IReadOnlyList<IReadOnlyList<Value>> Read(byte[] book, string sheet, string range,
        bool asText)
    {
        using var document = Open(book);
        var rows = new List<IReadOnlyList<Value>>();
        var page = Find(document, sheet);
        if (page is null)
        {
            // No name asked for means the first sheet, so a workbook with none at all is simply
            // empty rather than wrong. A name that is not there is a step asking for the wrong
            // thing, and says so.
            if (sheet.Length > 0)
            {
                throw NoSuchSheet(document, sheet);
            }

            return rows;
        }

        var used = page.RangeUsed();
        if (Area(page, range, used) is not { } area)
        {
            return rows;
        }

        var written = range.Trim().Length > 0;
        foreach (var line in area.Rows())
        {
            var cells = line.Cells().Select(cell => Held(cell, asText)).ToList();
            while (!written && cells.Count > 0 && cells[^1].AsText().Length == 0)
            {
                cells.RemoveAt(cells.Count - 1);
            }

            rows.Add(cells);
        }

        return rows;
    }

    /// <summary>
    /// The cells a written range names, or nothing when there is nothing to read. A range with no
    /// colon is the cell reading starts at: what a macro author means by <c>B2</c> is not the one
    /// cell, it is everything from there down and to the right, and the used cells say where that
    /// ends.
    /// </summary>
    private static IXLRange? Area(IXLWorksheet page, string range, IXLRange? used)
    {
        var written = range.Trim();
        if (written.Length == 0)
        {
            return used;
        }

        try
        {
            if (written.Contains(':'))
            {
                var halves = written.Split(':');
                if (halves.Length == 2 && halves[1].Trim().Length == 0)
                {
                    // "B2:" is "from B2 on", which only a used sheet can say the end of.
                    return used is null ? null : page.Range(page.Cell(halves[0].Trim()), used.LastCell());
                }

                return page.Range(written);
            }

            return used is null ? null : page.Range(page.Cell(written), used.LastCell());
        }
        catch (Exception refused) when (refused is not DeviceActionException)
        {
            // The name box's own words, handed back as a step that named cells the format has no
            // such place for: "A1:Z" or "row 3" is a name to fix, not a broken file.
            throw new DeviceActionException("Run.BadCellRange", written);
        }
    }

    /// <summary>
    /// The rows written into one sheet, handed back as the bytes of a workbook. Starting from
    /// nothing makes a new workbook; starting from the bytes of one keeps everything else that is
    /// in it and only touches the sheet that was asked for.
    /// </summary>
    /// <param name="book">The workbook to change, or <c>null</c> when there is no file yet.</param>
    /// <param name="sheet">Which sheet to write, or the first one when the name is empty.</param>
    /// <param name="rows">The rows to write, each a list of cells.</param>
    /// <param name="how">Where the rows go and what the cells they land in look like afterwards.</param>
    public static byte[] Write(byte[]? book, string sheet,
        IReadOnlyList<IReadOnlyList<Value>> rows, SheetWrite how)
    {
        using var document = book is null ? new XLWorkbook() : Open(book);

        // A sheet the step names is made when the workbook has no such sheet: writing where a macro
        // wants its table to go should not need a second step to put the sheet there, and no other
        // action adds one.
        var page = Find(document, sheet) ?? Add(document, sheet);
        var held = page.RangeUsed() is not null;

        // The names the sheet already holds have to be read before the contents go, and every cell
        // that is written needs one of them: without a name there is no column to put a value in,
        // and guessing a position is exactly what aligning by name was asked not to do.
        var standing = how.Align ? Names(page, how.HeaderRow) : [];
        if (standing.Count > 0)
        {
            var widest = rows.Count == 0 ? 0 : rows.Max(row => row.Count);
            if (how.Header is null || how.Header.Count < widest)
            {
                throw new DeviceActionException("Run.AlignNeedsNames",
                    widest.ToString(CultureInfo.InvariantCulture));
            }
        }

        if (how.Mode == SheetWriteMode.Replace)
        {
            // The contents go and the rest of the sheet stays: a header somebody coloured in is
            // still there afterwards, and a macro that only meant to replace the data has not
            // quietly thrown away the shape of the sheet.
            page.Clear(XLClearOptions.Contents);

            // The row of names is part of that shape, so a replace that cleared it puts it back
            // before the data goes under it.
            for (var at = 0; at < standing.Count; at++)
            {
                if (standing[at].Length > 0)
                {
                    Put(page.Cell(how.HeaderRow, at + 1), Value.FromText(standing[at]),
                        formulas: false, Format(how, at));
                }
            }
        }

        IReadOnlyList<IReadOnlyList<Value>> lines =
            how.Header is { Count: > 0 } && (how.Mode != SheetWriteMode.Append || !held)
                && standing.Count == 0
                ? [how.Header, .. rows]
                : rows;

        var corner = how.Mode switch
        {
            SheetWriteMode.Append => new CellRef(1, (page.LastRowUsed()?.RowNumber() ?? 0) + 1),

            // A replace that keeps the names writes nothing above the data: the row it starts on is
            // the one under them.
            SheetWriteMode.Replace when standing.Count > 0 => new CellRef(1, how.HeaderRow + 1),
            _ => Place(page, how.StartCell),
        };

        foreach (var row in lines)
        {
            for (var column = 0; column < row.Count; column++)
            {
                // Aligned, the column comes from the name rather than from where the value sits in
                // its row, so the starting cell only says which row this goes on.
                var at = standing.Count > 0
                    ? Named(standing, how.Header![column].AsText().Trim()) + 1
                    : corner.Column + column;
                var cell = page.Cell(corner.Row, at);
                Put(cell, row[column], how.Formulas, Format(how, column));
                Dress(cell, column, how);
            }

            corner = corner with { Row = corner.Row + 1 };
        }

        foreach (var column in page.ColumnsUsed())
        {
            if (how.Width > 0)
            {
                column.Width = how.Width;
            }

            if (how.AutoFit)
            {
                column.AdjustToContents();
            }
        }

        using var written = new MemoryStream();
        document.SaveAs(written);
        return written.ToArray();
    }

    /// <summary>Where writing goes, as a place on the grid rather than as a name.</summary>
    private readonly record struct CellRef(int Column, int Row);

    /// <summary>
    /// The names a row holds, from the first column on, with an empty name for every column nobody
    /// named. The gaps are kept because they are places too: a value whose name sits after one of
    /// them belongs to the column the name is in, not to the one next to the last name.
    /// </summary>
    private static IReadOnlyList<string> Names(IXLWorksheet page, int row)
    {
        if (row < 1)
        {
            return [];
        }

        var line = page.Row(row);
        var last = line.LastCellUsed()?.Address.ColumnNumber ?? 0;
        return last == 0
            ? []
            : [.. Enumerable.Range(1, last).Select(at => line.Cell(at).GetString().Trim())];
    }

    /// <summary>Which column a name sits above, counted from zero, or a step naming a column that is not there.</summary>
    private static int Named(IReadOnlyList<string> names, string wanted)
    {
        for (var at = 0; at < names.Count; at++)
        {
            if (string.Equals(names[at], wanted, StringComparison.OrdinalIgnoreCase))
            {
                return at;
            }
        }

        throw new DeviceActionException("Run.NoSuchColumn", wanted);
    }

    /// <summary>The cell a written place names, or the top left corner when none was named.</summary>
    private static CellRef Place(IXLWorksheet page, string startCell)
    {
        var written = startCell.Trim();
        if (written.Length == 0)
        {
            return new CellRef(1, 1);
        }

        try
        {
            var cell = page.Cell(written);
            return new CellRef(cell.Address.ColumnNumber, cell.Address.RowNumber);
        }
        catch (Exception refused) when (refused is not DeviceActionException)
        {
            throw new DeviceActionException("Run.BadCellRange", written);
        }
    }

    /// <summary>The names of the sheets, in the order they sit along the bottom of the window.</summary>
    public static IReadOnlyList<string> Sheets(byte[] book)
    {
        using var document = Open(book);
        return [.. document.Worksheets.Select(page => page.Name)];
    }

    /// <summary>
    /// Adds an empty sheet, handing back the workbook and whether one had to be made. A name that
    /// is already there is left alone rather than made twice: "open today's sheet" is a step a
    /// macro runs every morning, and the morning it runs twice must not be the morning it stops.
    /// </summary>
    public static (byte[] Book, bool Added) AddSheet(byte[]? book, string sheet)
    {
        using var document = book is null ? new XLWorkbook() : Open(book);
        if (Find(document, sheet) is not null)
        {
            return (Save(document), false);
        }

        Add(document, sheet);
        return (Save(document), true);
    }

    /// <summary>
    /// Takes a sheet out of a workbook, with everything in it. The last sheet is never removed:
    /// a workbook with no sheets is not something Excel will open, so the step is refused instead
    /// of the file being written that way.
    /// </summary>
    public static byte[] DeleteSheet(byte[] book, string sheet)
    {
        using var document = Open(book);
        var page = Find(document, sheet) ?? throw NoSuchSheet(document, sheet);
        if (document.Worksheets.Count <= 1)
        {
            throw new DeviceActionException("Run.LastSheet");
        }

        page.Delete();
        return Save(document);
    }

    /// <summary>Puts another name on a sheet, and writes nothing when the name is already taken.</summary>
    public static byte[] RenameSheet(byte[] book, string sheet, string name)
    {
        using var document = Open(book);
        var page = Find(document, sheet) ?? throw NoSuchSheet(document, sheet);
        if (Find(document, name) is { } taken && !ReferenceEquals(taken, page))
        {
            throw new DeviceActionException("Run.SheetNameTaken", name);
        }

        try
        {
            page.Name = name.Length == 0 ? page.Name : name;
        }
        catch (ArgumentException)
        {
            // Excel's own rules for a sheet name: no []:*?/\ and no more than 31 characters.
            throw new DeviceActionException("Run.BadSheetName", name);
        }

        return Save(document);
    }

    /// <summary>
    /// Makes room for rows by pushing what is under them down. An inserted row holds nothing, so
    /// this is one half of "put this line in before the fifth row": the write that comes after it
    /// fills the space in, and the row it pushes down is exactly why the insert is its own step
    /// rather than a setting on writing.
    /// </summary>
    public static byte[] InsertRows(byte[] book, string sheet, int at, int count)
    {
        using var document = Open(book);
        var page = Find(document, sheet) ?? throw NoSuchSheet(document, sheet);
        var (first, _) = Rows(at, count);

        // Every row shows up whatever is in it, so a row past the end of the table is still a row
        // the file has: inserting there is what a macro does to make room at the bottom.
        page.Row(first).InsertRowsAbove(count);
        return Save(document);
    }

    /// <summary>
    /// Shows a sheet or takes it off the tabs along the bottom. Hiding is how a workbook keeps its
    /// working-out out of the way: the draft a report is built from, or the list the report looks
    /// values up in, does not have to be on screen beside the report.
    /// </summary>
    /// <remarks>
    /// The last sheet that is on show never goes: a workbook whose sheets are all hidden is one
    /// nobody can unhide from Excel's own window — the menu that would do it is only offered when
    /// something is showing — so the step is refused rather than leaving a file that looks broken.
    /// </remarks>
    public static byte[] SetSheetVisibility(byte[] book, string sheet, SheetVisibility how)
    {
        using var document = Open(book);
        var page = Find(document, sheet) ?? throw NoSuchSheet(document, sheet);
        var showing = document.Worksheets.Count(other =>
            other.Visibility == XLWorksheetVisibility.Visible);
        if (how != SheetVisibility.Shown && page.Visibility == XLWorksheetVisibility.Visible
            && showing <= 1)
        {
            throw new DeviceActionException("Run.LastVisibleSheet", page.Name);
        }

        page.Visibility = how switch
        {
            SheetVisibility.Hidden => XLWorksheetVisibility.Hidden,
            SheetVisibility.VeryHidden => XLWorksheetVisibility.VeryHidden,
            _ => XLWorksheetVisibility.Visible,
        };

        return Save(document);
    }

    /// <summary>
    /// Puts a copy of a sheet into a workbook, with a name of its own: last month's sheet is the
    /// shape this month's is filled into, and a template is the same thing between files.
    /// </summary>
    /// <param name="source">The workbook the sheet comes from.</param>
    /// <param name="into">
    /// The workbook the copy goes into, or <c>null</c> for a new one. The same bytes as
    /// <paramref name="source"/> when the copy stays in the file it came from.
    /// </param>
    /// <param name="sheet">Which sheet to copy, or the first one when the name is empty.</param>
    /// <param name="name">What the copy is called, or the name it came from when nothing is said.</param>
    /// <param name="first">Whether the copy goes in front of the other sheets rather than behind them.</param>
    /// <returns>The workbook the copy went into, and the name it took.</returns>
    public static (byte[] Book, string Called) CopySheet(byte[] source, byte[]? into, string sheet,
        string name, bool first)
    {
        using var from = Open(source);
        var page = Find(from, sheet) ?? throw NoSuchSheet(from, sheet);
        var called = name.Length > 0 ? name : page.Name;

        using var document = into is null ? new XLWorkbook() : Open(into);
        if (Find(document, called) is not null)
        {
            throw new DeviceActionException("Run.SheetNameTaken", called);
        }

        var copy = page.CopyTo(document, called);
        copy.Position = first ? 1 : document.Worksheets.Count;
        return (Save(document), called);
    }

    /// <summary>
    /// Takes rows out and brings what is under them up. Deleting rows a sheet does not hold is not
    /// a failure: "clear yesterday's lines" runs on a sheet that may hold none of them.
    /// </summary>
    /// <returns>The workbook, and how many rows there were to take out.</returns>
    public static (byte[] Book, int Removed) DeleteRows(byte[] book, string sheet, int at, int count)
    {
        using var document = Open(book);
        var page = Find(document, sheet) ?? throw NoSuchSheet(document, sheet);
        var (first, last) = Rows(at, count);

        // Asking for more rows than the sheet holds takes out the ones it does hold: a loop that
        // clears a block does not have to know how big the block ended up being.
        var removed = Math.Min(last, page.LastRowUsed()?.RowNumber() ?? 0) - first + 1;
        if (removed <= 0)
        {
            return (book, 0);
        }

        page.Rows(first, first + removed - 1).Delete();
        return (Save(document), removed);
    }

    /// <summary>
    /// The rows a step named, as the first and the last of them. A row a sheet has not got is a
    /// step to fix rather than a file to write: rows are counted from one and a sheet stops long
    /// before the millionth, and everything past the last one is filled in the same way.
    /// </summary>
    private static (int First, int Last) Rows(int at, int count)
    {
        if (at < 1 || at > LastRow)
        {
            throw new DeviceActionException("Run.BadRow",
                at.ToString(CultureInfo.InvariantCulture));
        }

        if (count < 1)
        {
            throw new DeviceActionException("Run.BadRow",
                count.ToString(CultureInfo.InvariantCulture));
        }

        return (at, Math.Min(at + count - 1, LastRow));
    }

    /// <summary>The last row a sheet has, which is the format's own limit rather than a choice.</summary>
    private const int LastRow = 1048576;

    private static byte[] Save(XLWorkbook document)
    {
        using var written = new MemoryStream();
        document.SaveAs(written);
        return written.ToArray();
    }

    /// <summary>What one cell holds, in the kind of value the engine works with.</summary>
    private static Value Held(IXLCell cell, bool asText) => asText
        ? Value.FromText(cell.GetFormattedString())
        : cell.Value.Type switch
        {
            XLDataType.Boolean => Value.FromBool(cell.Value.GetBoolean()),
            XLDataType.Number => Value.FromNumber(cell.Value.GetNumber()),
            XLDataType.DateTime => Value.FromText(Moment(cell.Value.GetDateTime())),
            XLDataType.TimeSpan => Value.FromText(
                cell.Value.GetTimeSpan().ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)),

            // An error cell has no value to read, only what it says: #DIV/0! and the like, which is
            // what the person looking at the sheet sees and what a macro comparing against it expects.
            XLDataType.Error => Value.FromText(cell.GetFormattedString()),
            XLDataType.Text => Value.FromText(cell.Value.GetText()),
            _ => Value.FromText(string.Empty),
        };

    /// <summary>
    /// A date as text. A sheet has no date kind of its own as far as a macro is concerned, and the
    /// text a person reads is the day: a time of day alone is not written out with the day it is
    /// counted from, which is the day before 1900 in the format's own reckoning.
    /// </summary>
    private static string Moment(DateTime moment)
        => moment.TimeOfDay == TimeSpan.Zero
            ? moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Puts one cell's value into a sheet, as the kind of thing it is.</summary>
    private static void Put(IXLCell cell, Value value, bool formulas, string format)
    {
        switch (value.Kind)
        {
            case ValueKind.Number:
                cell.Value = value.Number;
                break;
            case ValueKind.Bool:
                cell.Value = value.Flag;
                break;
            case ValueKind.List:
                // A list is one item per line, the way it is written wherever else a list becomes
                // one piece of text; a nested table would need a shape a sheet cannot guess.
                cell.Value = value.AsText();
                break;

            // A column shown as a date has to hold dates, or the format has nothing to show: what a
            // macro reads back from a date column is the day as text, and writing that text into a
            // date column is the one place where reading it as a date is what was meant. Excel does
            // the same with what somebody types into a cell that is already formatted as a date.
            case ValueKind.Text when IsMoment(format) && Moment(value.Text, out var moment):
                cell.Value = moment;
                break;
            case ValueKind.Text when formulas && value.Text.StartsWith('=') && value.Text.Length > 1:
                // Without the "=", which is the sign that says "this is a formula" rather than part
                // of the formula itself.
                cell.FormulaA1 = value.Text[1..];
                break;
            default:
                if (value.Text.Length > 0)
                {
                    cell.Value = value.Text;
                }

                break;
        }
    }

    /// <summary>The format named for one written column, or nothing when that column has none.</summary>
    private static string Format(SheetWrite how, int column)
        => column < how.Formats.Count ? how.Formats[column] : string.Empty;

    /// <summary>
    /// Whether a format shows a day or a time rather than a plain number. Excel's format codes are
    /// letters — y, m, d, h, s — and nothing else in a format is: a number is made of # and 0, a
    /// share is %, and text is @ or a word in quotes, which is why the quotes are stepped over.
    /// </summary>
    private static bool IsMoment(string format)
    {
        var moment = false;
        var quoted = false;
        for (var at = 0; at < format.Length; at++)
        {
            if (format[at] == '"')
            {
                quoted = !quoted;
                continue;
            }

            if (format[at] == '\\')
            {
                // The character after a backslash is the character itself, letter or not.
                at++;
                continue;
            }

            if (!quoted && char.ToUpperInvariant(format[at]) is 'Y' or 'M' or 'D' or 'H' or 'S')
            {
                moment = true;
            }
        }

        return moment;
    }

    /// <summary>
    /// Whether a written cell holds a date. Both readings are tried because a date is written the
    /// way the file's own culture writes it when the day and the month could be either way round.
    /// </summary>
    private static bool Moment(string text, out DateTime moment)
        => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out moment)
            || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out moment);

    /// <summary>
    /// Puts on a written cell the things the step asked for: what its column is shown as, where the
    /// text sits in that column, and whether it may spill onto more than one line. A cell whose
    /// column was not named keeps looking exactly like the cell it was.
    /// </summary>
    private static void Dress(IXLCell cell, int column, SheetWrite how)
    {
        if (column < how.Formats.Count && how.Formats[column].Length > 0)
        {
            cell.Style.NumberFormat.Format = how.Formats[column];
        }

        if (how.AlignText != SheetTextAlign.Leave)
        {
            cell.Style.Alignment.Horizontal = how.AlignText switch
            {
                SheetTextAlign.Left => XLAlignmentHorizontalValues.Left,
                SheetTextAlign.Center => XLAlignmentHorizontalValues.Center,
                _ => XLAlignmentHorizontalValues.Right,
            };
        }

        if (how.WrapText)
        {
            cell.Style.Alignment.WrapText = true;
        }
    }

    /// <summary>How the rows a step writes go into the sheet.</summary>
    public enum SheetWriteMode
    {
        /// <summary>Clear the sheet's contents, then write from the starting cell.</summary>
        Replace,

        /// <summary>Write over the cells from the starting cell on, leaving the rest as it was.</summary>
        Insert,

        /// <summary>Write below the last row the sheet holds.</summary>
        Append,
    }

    /// <summary>
    /// How a step wants its rows written down: where they go, what happens to what the sheet already
    /// holds, and what the cells they land in look like afterwards. One place rather than a dozen
    /// arguments, because half of these are optional and in a list of arguments the order they came
    /// in says nothing about which is which.
    /// </summary>
    public sealed record SheetWrite
    {
        /// <summary>
        /// Column names to put above the rows, or nothing when the table has none. Reading a sheet
        /// hands the names back beside the rows rather than inside them, so writing one back needs a
        /// place to say where they go; adding to a sheet that already holds something does not write
        /// them again, and that is what keeps a log from growing a second header.
        /// </summary>
        public IReadOnlyList<Value>? Header { get; init; }

        /// <summary>Where the rows go, and what happens to what the sheet already holds.</summary>
        public SheetWriteMode Mode { get; init; } = SheetWriteMode.Replace;

        /// <summary>The cell the first row starts at, as <c>B2</c>.</summary>
        public string StartCell { get; init; } = "A1";

        /// <summary>
        /// Write a cell holding text that begins with <c>=</c> as a formula, the way Excel does when
        /// somebody types one. Off means every cell is written as the text or the number it is.
        /// </summary>
        public bool Formulas { get; init; }

        /// <summary>
        /// Put each value in the column whose name above it is the name this step gives that value,
        /// rather than at the same place across every row. The names the sheet already holds are read
        /// from <see cref="HeaderRow"/> before anything is written — a replace clears the very row
        /// they sit on — because a sheet somebody else maintains is one whose columns sit in an order
        /// of their own, and reading its table and writing it back has to follow the names rather
        /// than the positions. A sheet with no names yet takes the rows in the order they were
        /// written.
        /// </summary>
        public bool Align { get; init; }

        /// <summary>
        /// Which row of the sheet holds the column names, counted from the top. Only used when the
        /// values are put in by column name.
        /// </summary>
        public int HeaderRow { get; init; } = 1;

        /// <summary>
        /// What each column of the written rows is shown as, in Excel's own words for a number
        /// format: <c>yyyy-mm-dd</c> for a date, <c>#,##0.00</c> for money, <c>0%</c> for a share,
        /// <c>@</c> to hold text as text. One per column, counted from the left of what this step
        /// writes; a column past the end of the list keeps whatever it had, so an empty list leaves
        /// every cell as it is.
        /// </summary>
        public IReadOnlyList<string> Formats { get; init; } = [];

        /// <summary>Where the text of the written cells sits across their column.</summary>
        public SheetTextAlign AlignText { get; init; }

        /// <summary>Let a written cell show its text on more than one line.</summary>
        public bool WrapText { get; init; }

        /// <summary>
        /// How wide each written column is made, or 0 to leave the widths alone. The automatic width
        /// is asked for after this, so it is the one that wins when both are set.
        /// </summary>
        public double Width { get; init; }

        /// <summary>Widen the columns to show what was written.</summary>
        public bool AutoFit { get; init; }
    }

    /// <summary>Where a written cell's text sits across its column.</summary>
    public enum SheetTextAlign
    {
        /// <summary>The alignment the cell already has.</summary>
        Leave,

        Left,
        Center,
        Right,
    }

    /// <summary>Whether a sheet is on the tabs along the bottom of the window.</summary>
    public enum SheetVisibility
    {
        /// <summary>On the tabs, which is where every sheet starts.</summary>
        Shown,

        /// <summary>Off the tabs, put back from Excel's own menu.</summary>
        Hidden,

        /// <summary>Off the tabs and out of that menu, so only code can put it back.</summary>
        VeryHidden,
    }

    /// <summary>The sheet a name stands for, or nothing when the workbook has no such sheet.</summary>
    private static IXLWorksheet? Find(XLWorkbook document, string name)
        => name.Length == 0
            ? document.Worksheets.FirstOrDefault()
            : document.Worksheets.FirstOrDefault(page =>
                string.Equals(page.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes a sheet, or says why the name it was given cannot be one.</summary>
    private static IXLWorksheet Add(XLWorkbook document, string name)
    {
        var wanted = name.Length == 0 ? "Sheet1" : name;
        try
        {
            return document.AddWorksheet(wanted);
        }
        catch (ArgumentException)
        {
            // Excel's own rules for a sheet name: no []:*?/\ and no more than 31 characters. A
            // macro author who typed one of those has a name to fix, not a file that is broken.
            throw new DeviceActionException("Run.BadSheetName", wanted);
        }
    }

    /// <summary>
    /// Whether a file is the format Excel wrote before 2007: an OLE container of records rather
    /// than a zip of XML, which is a shape ClosedXML does not read at all. What decides is the
    /// bytes rather than the name the step gave the file — a macro that says <c>.xls</c> and hands
    /// over something else is a thing that happens, and the bytes are the honest answer.
    /// </summary>
    private static bool Old(byte[] book)
        => book.Length >= 8 && book[0] == 0xD0 && book[1] == 0xCF
            && book[2] == 0x11 && book[3] == 0xE0;

    /// <summary>
    /// Says a workbook in that format cannot be read here. Reading one would mean carrying a second
    /// reader for a format Excel stopped writing in 2007, and what the person holding that file
    /// needs is the one sentence — save it as .xlsx and use that copy — rather than a general
    /// "this is not a workbook", which would be the wrong thing to say about a spreadsheet.
    /// </summary>
    private static DeviceActionException OldFormatReadOnly() => new("Run.OldFormatReadOnly");

    /// <summary>
    /// Opens a workbook from the bytes of a file, or says why it cannot be opened. A file from
    /// before 2007 gets its own sentence; everything else the format's reader refuses — a text file
    /// somebody renamed, a download that stopped halfway — is answered with "not a workbook", which
    /// is the part a macro author can act on.
    /// </summary>
    private static XLWorkbook Open(byte[] book)
    {
        if (Old(book))
        {
            throw OldFormatReadOnly();
        }

        return Open(new MemoryStream(book));
    }

    /// <summary>
    /// Opens a workbook, or says the file is not one. What the format's reader says about a file
    /// that is not a workbook at all — a text file somebody renamed, a download that stopped
    /// halfway — is nothing a macro author can act on; that it is not a workbook is.
    /// </summary>
    private static XLWorkbook Open(Stream stream)
    {
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception refused) when (refused is not DeviceActionException)
        {
            throw NotAWorkbook();
        }
    }

    /// <summary>A step named a sheet the workbook does not have.</summary>
    private static DeviceActionException NoSuchSheet(XLWorkbook document, string name)
    {
        var have = string.Join(", ", document.Worksheets.Select(page => page.Name));
        return new DeviceActionException("Run.NoSuchSheet", $"{name} (this workbook has: {have})");
    }

    /// <summary>A file that cannot be read as a workbook at all.</summary>
    private static DeviceActionException NotAWorkbook() => new("Run.NotAWorkbook");
}
