using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// A workbook read and written as a file. An .xlsx is a zip of XML parts, and doing the reading
/// and writing here rather than by driving Excel is what lets a macro touch a workbook on a
/// machine with no Office on it at all, without a window opening that somebody has to close.
/// </summary>
/// <remarks>
/// The work is done with the Open XML SDK, the format's own library: the zip container, the
/// relationships between the parts and the XML schema are its business, so what is left here is
/// only the part that means something to a macro author — which sheet, and which cells.
///
/// Every cell comes back as text, the same way reading a CSV does, so the rows can be handed
/// straight to the actions that work on lists. A cell whose number format says it is a date is
/// turned into that date rather than left as the serial number it is stored as, because nobody who
/// typed a date into a sheet wants to read back "45123".
/// </remarks>
public static class Spreadsheet
{
    /// <summary>
    /// The used cells of one sheet, as rows of text. A row is as wide as its last cell that holds
    /// something, and the sheet stops at its last row that does, so what comes back is the block
    /// somebody typed rather than the empty grid the file pads itself out to.
    /// </summary>
    /// <param name="book">The bytes of the workbook, as it sits on disk.</param>
    /// <param name="sheet">
    /// Which sheet to read. An empty name means the first one, which is the one a person looking
    /// at the file sees first.
    /// </param>
    public static IReadOnlyList<IReadOnlyList<string>> Read(byte[] book, string sheet)
    {
        using var stream = new MemoryStream(book);
        using var document = Open(stream, false);
        var workbook = document.WorkbookPart ?? throw NotAWorkbook();
        var contents = workbook.Workbook ?? throw NotAWorkbook();
        var chosen = Find(contents, sheet);
        if (chosen is null)
        {
            // No name asked for means the first sheet, so a workbook with none at all is simply
            // empty rather than wrong. A name that is not there is a step asking for the wrong
            // thing, and says so.
            if (sheet.Length > 0)
            {
                throw NoSuchSheet(contents, sheet);
            }

            return [];
        }

        if (Page(workbook, chosen) is not { } page)
        {
            return [];
        }

        var shared = workbook.SharedStringTablePart?.SharedStringTable is { } table
            ? table.Elements<SharedStringItem>().Select(item => item.InnerText).ToArray()
            : [];
        var styles = workbook.WorkbookStylesPart?.Stylesheet;
        var formats = styles?.CellFormats?.Elements<CellFormat>().ToList() ?? [];
        var written = styles?.NumberingFormats?.Elements<NumberingFormat>()
            .Where(format => format.NumberFormatId is not null && format.FormatCode is not null)
            .ToDictionary(format => format.NumberFormatId!.Value, format => format.FormatCode!.Value!)
            ?? [];
        var from1904 = contents.WorkbookProperties?.Date1904?.Value ?? false;

        var rows = new List<List<string>>();
        foreach (var row in page.GetFirstChild<SheetData>()?.Elements<Row>() ?? [])
        {
            var cells = new List<string>();
            var at = 0;
            foreach (var cell in row.Elements<Cell>())
            {
                // A cell says where it is, so a run of empty cells between two that hold something
                // is simply missing from the file and has to be filled back in by position.
                if (cell.CellReference?.Value is { } reference)
                {
                    at = Math.Max(at, ColumnOf(reference));
                }

                while (cells.Count < at)
                {
                    cells.Add(string.Empty);
                }

                cells.Add(TextOf(cell, shared, formats, written, from1904));
                at++;
            }

            while (cells.Count > 0 && cells[^1].Length == 0)
            {
                cells.RemoveAt(cells.Count - 1);
            }

            rows.Add(cells);
        }

        // A sheet keeps trailing rows that were typed into once and emptied later; they are not
        // part of what a person reads as the sheet's contents, and would only add blank rows to a
        // loop that walks them.
        while (rows.Count > 0 && rows[^1].Count == 0)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return rows;
    }

    /// <summary>
    /// The rows written into one sheet, handed back as the bytes of a workbook. Starting from
    /// nothing makes a new workbook; starting from the bytes of one keeps everything else that is
    /// in it and only touches the sheet that was asked for.
    /// </summary>
    /// <param name="book">The workbook to change, or <c>null</c> when there is no file yet.</param>
    /// <param name="sheet">Which sheet to write, or the first one when the name is empty.</param>
    /// <param name="rows">The rows to write, each a list of cells.</param>
    /// <param name="append">
    /// Add the rows below what the sheet already holds instead of replacing it, which is how a
    /// macro keeps a log in a sheet across runs.
    /// </param>
    public static byte[] Write(byte[]? book, string sheet, IReadOnlyList<IReadOnlyList<string>> rows,
        bool append)
    {
        var stream = new MemoryStream();
        if (book is not null)
        {
            stream.Write(book, 0, book.Length);
        }

        stream.Position = 0;

        using (var document = book is null
            ? SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true)
            : Open(stream, true))
        {
            var workbook = document.WorkbookPart;
            Workbook contents;
            if (workbook?.Workbook is { } loaded)
            {
                contents = loaded;
            }
            else
            {
                (workbook, contents) = Begin(document);
            }

            var sheets = contents.Sheets ?? contents.AppendChild(new Sheets());

            // A sheet the step names is made when the workbook has no such sheet: writing where a
            // macro wants its table to go should not need a second step to put the sheet there,
            // and no other action adds one.
            var chosen = Find(contents, sheet)
                ?? Add(workbook, sheets, sheet.Length == 0 ? "Sheet1" : sheet).Sheet;
            if (Page(workbook, chosen) is not { } page)
            {
                throw NotAWorkbook();
            }

            var sheetData = page.GetFirstChild<SheetData>() ?? page.AppendChild(new SheetData());
            if (!append)
            {
                sheetData.RemoveAllChildren<Row>();
            }

            var number = append ? Last(sheetData) : 0u;
            foreach (var row in rows)
            {
                number++;
                sheetData.AppendChild(Compose(number, row));
            }

            page.Save();
        }

        return stream.ToArray();
    }

    /// <summary>The sheet a name stands for, or nothing when the workbook has no such sheet.</summary>
    private static Sheet? Find(Workbook contents, string name)
    {
        var sheets = contents.Sheets;
        if (sheets is null)
        {
            return null;
        }

        return name.Length == 0
            ? sheets.Elements<Sheet>().FirstOrDefault()
            : sheets.Elements<Sheet>().FirstOrDefault(sheet =>
                string.Equals(sheet.Name?.Value, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Where a sheet's cells are kept.</summary>
    private static Worksheet? Page(WorkbookPart workbook, Sheet sheet)
        => sheet.Id?.Value is { } id
            ? (workbook.GetPartById(id) as WorksheetPart)?.Worksheet
            : null;

    /// <summary>Makes a new sheet in a workbook and hands back where its cells live.</summary>
    private static (WorksheetPart Part, Sheet Sheet) Add(WorkbookPart workbook, Sheets sheets, string name)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        part.Worksheet = new Worksheet(new SheetData());
        var highest = sheets.Elements<Sheet>().Select(sheet => sheet.SheetId?.Value ?? 0u)
            .DefaultIfEmpty(0u).Max();
        var added = new Sheet
        {
            Id = workbook.GetIdOfPart(part),
            SheetId = highest + 1,
            Name = name,
        };
        sheets.Append(added);
        return (part, added);
    }

    /// <summary>The start of a workbook that is being written for the first time.</summary>
    private static (WorkbookPart Part, Workbook Contents) Begin(SpreadsheetDocument document)
    {
        var part = document.AddWorkbookPart();
        var contents = new Workbook();
        part.Workbook = contents;
        contents.AppendChild(new Sheets());
        return (part, contents);
    }

    /// <summary>The row number new rows go after when a sheet is added to rather than replaced.</summary>
    private static uint Last(SheetData sheetData)
    {
        var rows = sheetData.Elements<Row>().ToList();
        return rows.Count == 0 ? 0u : rows[^1].RowIndex?.Value ?? (uint)rows.Count;
    }

    /// <summary>One row of cells, placed by column so a gap keeps everything after it lined up.</summary>
    private static Row Compose(uint number, IReadOnlyList<string> cells)
    {
        var row = new Row { RowIndex = number };
        for (var index = 0; index < cells.Count; index++)
        {
            if (cells[index].Length == 0)
            {
                continue;
            }

            row.AppendChild(new Cell
            {
                CellReference = $"{ColumnName(index)}{number}",
                DataType = CellValues.InlineString,
                InlineString = new InlineString(
                    new Text(cells[index]) { Space = SpaceProcessingModeValues.Preserve }),
            });
        }

        return row;
    }

    /// <summary>
    /// What one cell holds. Text is text however it was stored; a number is handed over as the
    /// number that was typed, except when its format says it is a date.
    /// </summary>
    private static string TextOf(Cell cell, string[] shared, IReadOnlyList<CellFormat> formats,
        Dictionary<uint, string> written, bool from1904)
    {
        var raw = cell.CellValue?.InnerText ?? string.Empty;
        if (Holds(cell, CellValues.SharedString))
        {
            return Shared(raw, shared);
        }

        if (Holds(cell, CellValues.InlineString))
        {
            return cell.InlineString?.InnerText ?? string.Empty;
        }

        if (Holds(cell, CellValues.Boolean))
        {
            return raw == "1" ? "TRUE" : "FALSE";
        }

        if (Holds(cell, CellValues.String) || Holds(cell, CellValues.Date) || Holds(cell, CellValues.Error))
        {
            return raw;
        }

        return Dated(raw, cell, formats, written, from1904);
    }

    /// <summary>
    /// Whether a cell holds one kind of thing. The SDK's cell types are a struct with one instance
    /// per kind rather than an enum, so they are compared rather than matched against.
    /// </summary>
    private static bool Holds(Cell cell, CellValues kind)
        => cell.DataType?.Value.Equals(kind) ?? false;

    private static string Shared(string raw, string[] table)
        => int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
           && index >= 0 && index < table.Length
            ? table[index]
            : raw;

    /// <summary>
    /// A number whose cell is formatted as a date is a date, and is handed over as the text of the
    /// day it stands for. The arithmetic is the format's own convention, including the 1904 system
    /// some workbooks made by an old Mac Excel still carry.
    /// </summary>
    private static string Dated(string raw, Cell cell, IReadOnlyList<CellFormat> formats,
        Dictionary<uint, string> written, bool from1904)
    {
        if (raw.Length == 0 || !IsDate(cell, formats, written)
            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            || serial < 0)
        {
            return raw;
        }

        DateTime moment;
        try
        {
            moment = DateTime.FromOADate(from1904 ? serial + 1462 : serial);
        }
        catch (ArgumentException)
        {
            return raw;
        }

        return moment.TimeOfDay == TimeSpan.Zero
            ? moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : moment.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    /// <summary>Whether the format of a cell's style reads as a date.</summary>
    private static bool IsDate(Cell cell, IReadOnlyList<CellFormat> formats, Dictionary<uint, string> written)
    {
        var index = (int)(cell.StyleIndex?.Value ?? 0u);
        if (index < 0 || index >= formats.Count)
        {
            return false;
        }

        var id = formats[index].NumberFormatId?.Value ?? 0u;

        // The ids below 50 are the ones the file format sets aside for dates and times in every
        // locale; anything else is a format somebody wrote, and has to be read.
        if (id is >= 14u and <= 22u or >= 45u and <= 47u)
        {
            return true;
        }

        return written.TryGetValue(id, out var code) && LooksLikeDate(code);
    }

    /// <summary>
    /// Whether a number format is a date format. What is inside quotes or square brackets is
    /// literal text — the format for a currency might spell out a date word — so only what is left
    /// over says anything, and a date format always has a year, day, hour or second in it.
    /// </summary>
    private static bool LooksLikeDate(string code)
        => Literal.Replace(code, string.Empty).IndexOfAny(Years) >= 0;

    private static readonly char[] Years = ['y', 'Y', 'd', 'D', 'h', 'H', 's', 'S'];

    /// <summary>Quoted text and bracketed sections, which say nothing about a value's kind.</summary>
    private static readonly Regex Literal = new("\"[^\"]*\"|\\[[^\\]]*\\]", RegexOptions.Compiled);

    /// <summary>The Excel name of a column: 0 is A, 25 is Z, 26 is AA.</summary>
    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var at = index; at >= 0; at = (at / 26) - 1)
        {
            name = (char)('A' + (at % 26)) + name;
        }

        return name;
    }

    /// <summary>The column a cell reference names: A1 is 0, B1 is 1, AA1 is 26.</summary>
    private static int ColumnOf(string reference)
    {
        var column = 0;
        foreach (var character in reference)
        {
            if (!char.IsLetter(character))
            {
                break;
            }

            column = (column * 26) + char.ToUpperInvariant(character) - 'A' + 1;
        }

        return column - 1;
    }

    /// <summary>A step named a sheet the workbook does not have.</summary>
    private static DeviceActionException NoSuchSheet(Workbook contents, string name)
    {
        var have = string.Join(", ", contents.Sheets?.Elements<Sheet>().Select(sheet => sheet.Name?.Value)
            ?? []);
        return new DeviceActionException("Run.NoSuchSheet", $"{name} (this workbook has: {have})");
    }

    /// <summary>
    /// Opens a workbook, or says the file is not one. What the package reader says about a file
    /// that is not a workbook at all — a text file somebody renamed, a download that stopped
    /// halfway — is nothing a macro author can act on; that it is not a workbook is.
    /// </summary>
    private static SpreadsheetDocument Open(Stream stream, bool writable)
    {
        try
        {
            return SpreadsheetDocument.Open(stream, writable);
        }
        catch (Exception refused) when (refused is OpenXmlPackageException
            or FileFormatException or InvalidDataException)
        {
            throw NotAWorkbook();
        }
    }

    /// <summary>A file that cannot be read as a workbook at all.</summary>
    private static DeviceActionException NotAWorkbook() => new("Run.NotAWorkbook");
}
