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
    public static IReadOnlyList<IReadOnlyList<Value>> Read(byte[] book, string sheet)
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

        if (page.RangeUsed() is not { } used)
        {
            return rows;
        }

        foreach (var line in used.Rows())
        {
            var cells = line.Cells().Select(Held).ToList();
            while (cells.Count > 0 && cells[^1].AsText().Length == 0)
            {
                cells.RemoveAt(cells.Count - 1);
            }

            rows.Add(cells);
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
    public static byte[] Write(byte[]? book, string sheet,
        IReadOnlyList<IReadOnlyList<Value>> rows, bool append)
    {
        using var document = book is null ? new XLWorkbook() : Open(new MemoryStream(book));

        // A sheet the step names is made when the workbook has no such sheet: writing where a macro
        // wants its table to go should not need a second step to put the sheet there, and no other
        // action adds one.
        var page = Find(document, sheet) ?? Add(document, sheet);
        if (!append)
        {
            // The contents go and the rest of the sheet stays: a header somebody coloured in is
            // still there afterwards, and a macro that only meant to replace the data has not
            // quietly thrown away the shape of the sheet.
            page.Clear(XLClearOptions.Contents);
        }

        var at = append ? (page.LastRowUsed()?.RowNumber() ?? 0) + 1 : 1;
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Count; column++)
            {
                Put(page.Cell(at, column + 1), row[column]);
            }

            at++;
        }

        using var written = new MemoryStream();
        document.SaveAs(written);
        return written.ToArray();
    }

    /// <summary>What one cell holds, in the kind of value the engine works with.</summary>
    private static Value Held(IXLCell cell) => cell.Value.Type switch
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
    private static void Put(IXLCell cell, Value value)
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
            default:
                if (value.Text.Length > 0)
                {
                    cell.Value = value.Text;
                }

                break;
        }
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
