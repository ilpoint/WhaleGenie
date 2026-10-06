using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Viktor.Core.Devices;

namespace Viktor.Core.Execution;

/// <summary>
/// Reading what the OCR saw: pulling the numbers out of what it read, and laying the pieces out
/// the way they sit on screen.
/// </summary>
/// <remarks>
/// Everything here works on what the device handed back and touches neither the screen nor the
/// engine, so the rules that are easy to get wrong — what counts as a number, and where one column
/// ends and the next begins — can be tested without a screen.
/// </remarks>
public static class OcrReading
{
    /// <summary>The narrowest gap that always counts as a column, however small the writing is.</summary>
    private const double MinimumColumnGap = 12;

    /// <summary>
    /// What counts as one column standing apart from the next inside a single reading: a tab, a
    /// wide space, or two spaces or more. One space is the gap between two words of a sentence.
    /// </summary>
    private static readonly Regex ColumnBreak = new(@"[\t\u3000]+| {2,}", RegexOptions.Compiled);

    /// <summary>
    /// The pieces that hold a number, each cut down to the number itself: the money sign, the
    /// thousands separators and the label around it are dropped, and a piece with no digit in it
    /// is left out altogether. That is what makes it useful on a screen full of words — the
    /// numbers of a list come back without the text between them.
    /// </summary>
    public static IReadOnlyList<TextSpan> Numbers(IReadOnlyList<TextSpan> spans)
    {
        var numbers = new List<TextSpan>();
        foreach (var span in spans)
        {
            if (Number(span.Text) is { } number)
            {
                numbers.Add(span with { Text = number });
            }
        }

        return numbers;
    }

    /// <summary>
    /// The number inside some text, or null when there is no digit in it at all. Digits written
    /// the full-width way, as a Chinese screen often shows them, are read as ordinary digits.
    /// </summary>
    public static string? Number(string text)
    {
        var written = new StringBuilder(text.Length);
        var seenDigit = false;

        foreach (var character in text)
        {
            var plain = Narrow(character);

            if (plain is >= '0' and <= '9')
            {
                written.Append(plain);
                seenDigit = true;
                continue;
            }

            // A minus sign only counts in front of the number, and a point only between two
            // digits; everything else around the number is left out.
            if (plain == '-' && !seenDigit && written.Length == 0)
            {
                written.Append(plain);
                continue;
            }

            if (plain == '.' && seenDigit && written[^1] != '.')
            {
                written.Append(plain);
            }
        }

        // A point left hanging at the end belongs to the text around the number, not the number.
        while (written.Length > 0 && written[^1] == '.')
        {
            written.Length--;
        }

        return seenDigit ? written.ToString() : null;
    }

    /// <summary>
    /// The pieces laid out the way they sit on screen: one row for each line of writing, one cell
    /// for each column in it. Rows and columns are worked out from where the pieces are, so a
    /// table printed without lines comes out the same as one with them, and the answer is the shape
    /// reading a table through UI Automation gives back.
    /// </summary>
    /// <remarks>Rows keep the order the pieces were read in, which is the order a person reads them.</remarks>
    public static IReadOnlyList<IReadOnlyList<string>> Rows(IReadOnlyList<TextSpan> spans)
    {
        var lines = new List<List<TextSpan>>();
        foreach (var span in spans)
        {
            var line = lines.Count > 0 ? lines[^1] : null;
            if (line is null || !SameLine(line[0], span))
            {
                lines.Add([span]);
                continue;
            }

            line.Add(span);
        }

        return [.. lines.Select(line => (IReadOnlyList<string>)Cells(line))];
    }

    /// <summary>Whether two pieces sit on the same line, judged by how much they overlap.</summary>
    private static bool SameLine(TextSpan first, TextSpan second)
    {
        var overlap = Math.Min(first.Location.Y + first.Size.Height,
            second.Location.Y + second.Size.Height) - Math.Max(first.Location.Y, second.Location.Y);

        return overlap >= Math.Min(first.Size.Height, second.Size.Height) * 0.5;
    }

    /// <summary>
    /// One line's pieces broken into cells: left to right, with a new cell wherever the gap is wide
    /// enough to be a column rather than the space between two words.
    /// </summary>
    private static List<string> Cells(IReadOnlyList<TextSpan> line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var right = 0d;

        foreach (var span in line.OrderBy(span => span.Location.X))
        {
            var gap = span.Location.X - right;
            var firstPiece = true;

            foreach (var piece in Pieces(span.Text))
            {
                // A piece that came out of one reading after a wide gap was a column of its own.
                // The reading's first piece starts a new cell only when the reading itself stands a
                // column away from whatever came before it.
                if (cell.Length > 0 && (!firstPiece || gap > ColumnGap(span.Size.Height)))
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else if (cell.Length > 0)
                {
                    cell.Append(' ');
                }

                cell.Append(piece);
                firstPiece = false;
            }

            right = span.Location.X + span.Size.Width;
        }

        if (cell.Length > 0)
        {
            cells.Add(cell.ToString());
        }

        return cells;
    }

    /// <summary>How wide a gap has to be to count as a column rather than a space between words.</summary>
    private static double ColumnGap(double height) => Math.Max(MinimumColumnGap, height * 0.75);

    /// <summary>
    /// The pieces of one reading, split where the writing stands well apart: an OCR engine hands
    /// back a whole line as one reading, and the wide gaps it left in the text are where the columns
    /// were.
    /// </summary>
    private static IEnumerable<string> Pieces(string text)
        => ColumnBreak.Split(text)
            .Select(piece => piece.Trim())
            .Where(piece => piece.Length > 0);

    /// <summary>
    /// The plain character behind a full-width one. A Chinese screen is full of them: OCR reads the
    /// numbers of a form as １２３ unless they are brought back to the ordinary digits.
    /// </summary>
    private static char Narrow(char character) => character switch
    {
        >= '\uFF10' and <= '\uFF19' => (char)(character - '\uFF10' + '0'),
        '\uFF0D' or '\u2212' => '-',
        '\uFF0E' => '.',
        _ => character,
    };
}
