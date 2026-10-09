using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// Text with cells separated by a character, read into rows and written back out. This is what a
/// file another program wrote looks like, and the shape of it is small enough to be tempting to
/// hand-roll — the quotes, the doubled quotes inside them, the line breaks that belong to a cell,
/// the file that ends in the middle of one — and every one of those is a way for a macro to read
/// the wrong data without anything failing.
/// </summary>
/// <remarks>
/// CsvHelper (Apache-2.0 / MS-PL) does the reading and the writing, so what is left here is only
/// the part that means something to a macro author: which character separates the cells, whether
/// blank lines are rows, whether spaces around a cell are part of it, and how the file ends its
/// lines. It is the most used library for this in .NET and it is pure managed code.
///
/// Nothing is thrown at the caller for text that does not look like a table. A file with an odd
/// quote in it, or with different numbers of cells from one row to the next, is still the file the
/// macro asked for, and a macro author cannot act on "the reader did not like row 40": the row is
/// handed over as it was written and the step goes on.
/// </remarks>
public static class DelimitedFile
{
    /// <summary>The rows of a file, each a list of the cells in it.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> Read(string text, string separator,
        bool skipBlankLines, bool trim)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = separator,

            // The header is the step's business, not the reader's: whether the first line holds
            // names is something the macro author says, and what they are is a list to hand back.
            HasHeaderRecord = false,
            IgnoreBlankLines = skipBlankLines,
            TrimOptions = trim ? TrimOptions.Trim : TrimOptions.None,
            BadDataFound = null,
            MissingFieldFound = null,
            DetectColumnCountChanges = false,
        };

        using var reader = new StringReader(text);
        using var csv = new CsvReader(reader, configuration);

        var rows = new List<IReadOnlyList<string>>();
        while (csv.Parser.Read())
        {
            rows.Add(csv.Parser.Record ?? []);
        }

        return rows;
    }

    /// <summary>The text of a file holding those rows.</summary>
    public static string Write(IReadOnlyList<IReadOnlyList<string>> rows, string separator,
        string lineEnding, bool quoteAll)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = separator,
            HasHeaderRecord = false,
            NewLine = lineEnding,

            // A cell is a cell. Escaping the ones that begin with "=" or "@" is a guard against
            // a spreadsheet treating somebody's text as a formula, and it changes the bytes of a
            // file this step was asked to write as it was given them.
            InjectionOptions = InjectionOptions.None,
        };

        if (quoteAll)
        {
            configuration.ShouldQuote = _ => true;
        }

        var text = new StringBuilder();
        using (var writer = new StringWriter(text, CultureInfo.InvariantCulture))
        using (var csv = new CsvWriter(writer, configuration))
        {
            foreach (var row in rows)
            {
                foreach (var cell in row)
                {
                    csv.WriteField(cell);
                }

                csv.NextRecord();
            }

            csv.Flush();
        }

        return text.ToString();
    }
}
