using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using ExcelDataReader;
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
        => Old(book)
            ? ReadOld(book, sheet, range)
            : ReadModern(book, sheet, range, asText);

    private static IReadOnlyList<IReadOnlyList<Value>> ReadModern(byte[] book, string sheet,
        string range, bool asText)
    {
        using var stream = new MemoryStream(book);
        using var document = Open(stream);
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
    /// <param name="header">
    /// Column names to put above the rows, or nothing when the table has none. Reading a sheet
    /// hands the names back beside the rows rather than inside them, so writing one back needs a
    /// place to say where they go; adding to a sheet that already holds something does not write
    /// them again, and that is what keeps a log from growing a second header.
    /// </param>
    /// <param name="mode">Where the rows go, and what happens to what the sheet already holds.</param>
    /// <param name="startCell">The cell the first row starts at, as <c>B2</c>.</param>
    /// <param name="formulas">
    /// Write a cell holding text that begins with <c>=</c> as a formula, the way Excel does when
    /// somebody types one. Off means every cell is written as the text or the number it is.
    /// </param>
    /// <param name="autoFit">Widen the columns to show what was written.</param>
    public static byte[] Write(byte[]? book, string sheet,
        IReadOnlyList<IReadOnlyList<Value>> rows, IReadOnlyList<Value>? header, SheetWriteMode mode,
        string startCell, bool formulas, bool autoFit)
    {
        if (book is not null && Old(book))
        {
            throw OldFormatReadOnly();
        }

        using var document = book is null ? new XLWorkbook() : Open(new MemoryStream(book));

        // A sheet the step names is made when the workbook has no such sheet: writing where a macro
        // wants its table to go should not need a second step to put the sheet there, and no other
        // action adds one.
        var page = Find(document, sheet) ?? Add(document, sheet);
        var held = page.RangeUsed() is not null;
        if (mode == SheetWriteMode.Replace)
        {
            // The contents go and the rest of the sheet stays: a header somebody coloured in is
            // still there afterwards, and a macro that only meant to replace the data has not
            // quietly thrown away the shape of the sheet.
            page.Clear(XLClearOptions.Contents);
        }

        IReadOnlyList<IReadOnlyList<Value>> lines =
            header is { Count: > 0 } && (mode != SheetWriteMode.Append || !held)
                ? [header, .. rows]
                : rows;

        var corner = mode == SheetWriteMode.Append
            ? new CellRef(1, (page.LastRowUsed()?.RowNumber() ?? 0) + 1)
            : Place(page, startCell);

        foreach (var row in lines)
        {
            for (var column = 0; column < row.Count; column++)
            {
                Put(page.Cell(corner.Row, corner.Column + column), row[column], formulas);
            }

            corner = corner with { Row = corner.Row + 1 };
        }

        if (autoFit)
        {
            foreach (var column in page.ColumnsUsed())
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
        => Old(book) ? OldSheets(book) : ModernSheets(book);

    private static IReadOnlyList<string> ModernSheets(byte[] book)
    {
        using var document = Open(new MemoryStream(book));
        return [.. document.Worksheets.Select(page => page.Name)];
    }

    /// <summary>
    /// Adds an empty sheet, handing back the workbook and whether one had to be made. A name that
    /// is already there is left alone rather than made twice: "open today's sheet" is a step a
    /// macro runs every morning, and the morning it runs twice must not be the morning it stops.
    /// </summary>
    public static (byte[] Book, bool Added) AddSheet(byte[]? book, string sheet)
    {
        if (book is not null && Old(book))
        {
            throw OldFormatReadOnly();
        }

        using var document = book is null ? new XLWorkbook() : Open(new MemoryStream(book));
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
        if (Old(book))
        {
            throw OldFormatReadOnly();
        }

        using var document = Open(new MemoryStream(book));
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
        if (Old(book))
        {
            throw OldFormatReadOnly();
        }

        using var document = Open(new MemoryStream(book));
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
    private static void Put(IXLCell cell, Value value, bool formulas)
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

    // -------------------------------------------------------------- the old format

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
    /// The rows of an old workbook, read by the reader for that format (ExcelDataReader). Reading
    /// is all that is on offer: files like this are what people have left over from an older
    /// Office, and what a macro does with one is read it — a step that asks to write one is told
    /// what to do instead rather than handed a file nobody can open.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<Value>> ReadOld(byte[] book, string sheet,
        string range)
    {
        Register();
        var names = OldSheets(book);
        if (sheet.Length > 0 && !names.Contains(sheet, StringComparer.OrdinalIgnoreCase))
        {
            throw new DeviceActionException("Run.NoSuchSheet",
                $"{sheet} (this workbook has: {string.Join(", ", names)})");
        }

        var wanted = sheet.Length > 0 ? sheet : names[0];
        var corner = Corner(range);
        var rows = new List<IReadOnlyList<Value>>();

        using var stream = new MemoryStream(book);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        do
        {
            if (!reader.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var line = 0;
            while (reader.Read())
            {
                line++;
                if (line < corner.Top)
                {
                    continue;
                }

                if (corner.Bottom > 0 && line > corner.Bottom)
                {
                    break;
                }

                var last = corner.Right > 0 ? corner.Right : reader.FieldCount;
                var cells = new List<Value>();
                for (var column = corner.Left; column <= last; column++)
                {
                    cells.Add(OldCell(reader, column));
                }

                // A written range is read as wide as it was asked for, the same as the modern
                // reader reads one; a row nobody bounded is read as wide as the row really is.
                if (corner.Right == 0)
                {
                    while (cells.Count > 0 && cells[^1].AsText().Length == 0)
                    {
                        cells.RemoveAt(cells.Count - 1);
                    }
                }

                rows.Add(cells);
            }

            break;
        }
        while (reader.NextResult());

        return rows;
    }

    /// <summary>The names of the sheets of an old workbook, in the order they sit in.</summary>
    private static IReadOnlyList<string> OldSheets(byte[] book)
    {
        Register();
        var names = new List<string>();
        using var reader = ExcelReaderFactory.CreateReader(new MemoryStream(book));
        do
        {
            names.Add(reader.Name);
        }
        while (reader.NextResult());

        return names;
    }

    /// <summary>What one cell of an old workbook holds, in the kind of value the engine works with.</summary>
    private static Value OldCell(IExcelDataReader reader, int column) => column > reader.FieldCount
        ? Value.FromText(string.Empty)
        : reader.GetValue(column - 1) switch
        {
            null => Value.FromText(string.Empty),
            double number => Value.FromNumber(number),
            bool flag => Value.FromBool(flag),
            DateTime moment => Value.FromText(Moment(moment)),
            TimeSpan span => Value.FromText(span.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)),

            // Anything else the reader hands over — the text of an error cell, most of all — comes
            // through as what it says, which is what the person looking at the sheet sees.
            var other => Value.FromText(other.ToString() ?? string.Empty),
        };

    /// <summary>Where a written range starts and stops, as the numbers of the grid.</summary>
    private readonly record struct Bounds(int Left, int Top, int Right, int Bottom);

    /// <summary>
    /// A range written the way the name box writes one, as numbers: a zero on the right or at the
    /// bottom means "as far as the sheet goes", which is what a range naming only its start means.
    /// </summary>
    private static Bounds Corner(string range)
    {
        var written = range.Trim();
        if (written.Length == 0)
        {
            return new Bounds(1, 1, 0, 0);
        }

        var halves = written.Split(':');
        var first = Grid(halves[0]);
        if (halves.Length == 1 || halves[1].Trim().Length == 0)
        {
            return new Bounds(first.Column, first.Row, 0, 0);
        }

        var second = Grid(halves[1]);
        return new Bounds(first.Column, first.Row, second.Column, second.Row);
    }

    /// <summary>One end of a range, from the letters and the number a person writes it with.</summary>
    private static CellRef Grid(string written)
    {
        var text = written.Trim().Replace("$", string.Empty, StringComparison.Ordinal);
        var letters = new string([.. text.TakeWhile(char.IsLetter)]);
        var digits = new string([.. text.SkipWhile(char.IsLetter)]);
        if (letters.Length == 0
            || digits.Length == 0
            || !int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row))
        {
            throw new DeviceActionException("Run.BadCellRange", written);
        }

        var column = 0;
        foreach (var letter in letters.ToUpperInvariant())
        {
            if (letter is < 'A' or > 'Z')
            {
                throw new DeviceActionException("Run.BadCellRange", written);
            }

            column = (column * 26) + (letter - 'A' + 1);
        }

        return new CellRef(column, row);
    }

    /// <summary>
    /// Registers the code pages an old file's text may be written in. They are the machine's own —
    /// GBK on a Chinese Windows — and the runtime leaves them out until it is asked for them.
    /// </summary>
    private static void Register()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Says an old workbook cannot be changed. Reading one is what a macro does with the files left
    /// over from an older Office, and writing one would mean a second library that can write that
    /// format; "save it as .xlsx" is one click for whoever has the file.
    /// </summary>
    private static DeviceActionException OldFormatReadOnly() => new("Run.OldFormatReadOnly");

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
