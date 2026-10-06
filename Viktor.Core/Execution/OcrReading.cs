using System.Collections.Generic;
using System.Text;

using Viktor.Core.Devices;

namespace Viktor.Core.Execution;

/// <summary>
/// Reading what the OCR saw: pulling the numbers out of what it read, and laying the pieces out
/// the way they sit on screen.
/// </summary>
public static class OcrReading
{
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
    /// The plain character behind a full-width one. A Chinese screen is full of them: OCR reads
    /// the numbers of a form as １２３ unless they are brought back to the ordinary digits.
    /// </summary>
    private static char Narrow(char character) => character switch
    {
        >= '\uFF10' and <= '\uFF19' => (char)(character - '\uFF10' + '0'),
        '\uFF0D' or '\u2212' => '-',
        '\uFF0E' => '.',
        _ => character,
    };
}
