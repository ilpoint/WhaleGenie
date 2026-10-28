using System;
using Avalonia.Input;
using WhaleGenie.Runtime;

namespace WhaleGenie.Views;

/// <summary>Turns one editor key press into the literal character a typed string would receive.</summary>
internal static class HotstringKeyCapture
{
    public static string? Character(Key key, KeyModifiers modifiers)
    {
        var shifted = modifiers.HasFlag(KeyModifiers.Shift);
        var name = key.ToString();
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            return shifted ? char.ToUpperInvariant(name[0]).ToString()
                : char.ToLowerInvariant(name[0]).ToString();
        }

        if (name.StartsWith('D') && name.Length == 2 && char.IsAsciiDigit(name[1]))
        {
            return Digit(name[1], shifted);
        }

        if (name.StartsWith("NumPad", StringComparison.Ordinal)
            && name.Length == 7 && char.IsAsciiDigit(name[^1]))
        {
            return name[^1].ToString();
        }

        var character = key switch
        {
            Key.Space => " ",
            Key.Return or Key.Enter => "\n",
            Key.Tab => "\t",
            Key.OemTilde => shifted ? "~" : "`",
            Key.OemMinus => shifted ? "_" : "-",
            Key.OemPlus => shifted ? "+" : "=",
            Key.OemOpenBrackets => shifted ? "{" : "[",
            Key.OemCloseBrackets => shifted ? "}" : "]",
            Key.OemPipe or Key.OemBackslash => shifted ? "|" : "\\",
            Key.OemSemicolon => shifted ? ":" : ";",
            Key.OemQuotes => shifted ? "\"" : "'",
            Key.OemComma => shifted ? "<" : ",",
            Key.OemPeriod => shifted ? ">" : ".",
            Key.OemQuestion => shifted ? "?" : "/",
            _ => null,
        };

        return character is { Length: 1 } && HotstringCharacters.IsAllowed(character[0])
            ? character
            : null;
    }

    private static string Digit(char digit, bool shifted)
    {
        if (!shifted)
        {
            return digit.ToString();
        }

        return digit switch
        {
            '1' => "!",
            '2' => "@",
            '3' => "#",
            '4' => "$",
            '5' => "%",
            '6' => "^",
            '7' => "&",
            '8' => "*",
            '9' => "(",
            '0' => ")",
            _ => digit.ToString(),
        };
    }
}
