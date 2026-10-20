using System.Collections.Generic;
using Avalonia.Input;

namespace WhaleGenie.Views;

/// <summary>
/// Turns Avalonia's captured key names into the names the global hook reports. Several keys have
/// different spellings in the two libraries, especially the numeric keypad.
/// </summary>
public static class HotkeyNameMapper
{
    public static string Chord(Key key, KeyModifiers held)
    {
        var parts = new List<string>();
        if (held.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }
        if (held.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }
        if (held.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }
        if (held.HasFlag(KeyModifiers.Meta))
        {
            parts.Add("Win");
        }

        parts.Add(Name(key));
        return string.Join("+", parts);
    }

    public static string Name(Key key)
    {
        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)key - (int)Key.D0).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return "NumPad" + ((int)key - (int)Key.NumPad0).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        }

        return key switch
        {
            Key.LeftCtrl => "左Ctrl",
            Key.RightCtrl => "右Ctrl",
            Key.LeftShift => "左Shift",
            Key.RightShift => "右Shift",
            Key.LeftAlt => "左Alt",
            Key.RightAlt => "右Alt",
            Key.LWin => "左Win",
            Key.RWin => "右Win",
            Key.Return => "Enter",
            Key.Back => "Backspace",
            Key.Escape => "Esc",
            Key.Space => "Space",
            Key.Add => "NumPadAdd",
            Key.Subtract => "NumPadSubtract",
            Key.Multiply => "NumPadMultiply",
            Key.Divide => "NumPadDivide",
            Key.Decimal => "NumPadDecimal",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemTilde => "`",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemPipe or Key.OemBackslash => "\\",
            _ => key.ToString(),
        };
    }
}
