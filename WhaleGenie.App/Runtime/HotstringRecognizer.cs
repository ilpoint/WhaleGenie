using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>A hotstring that just matched, and the input cleanup that goes with it.</summary>
public sealed record HotstringHit(
    TriggerEntry Entry,
    string EndingText,
    string EndingKey,
    bool SuppressEndingKey,
    int Backspaces,
    bool ReplayEndingKey);

/// <summary>
/// The short in-memory text buffer behind typed-string triggers. It never leaves this process and
/// only sees characters produced by the keyboard hook; input-method candidates are not involved.
/// </summary>
public sealed class HotstringRecognizer
{
    public const int MaximumAbbreviationLength = HotstringCharacters.MaximumLength;
    private readonly object _gate = new();
    private readonly StringBuilder _text = new();

    public string Buffer
    {
        get
        {
            lock (_gate)
            {
                return _text.ToString();
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _text.Clear();
        }
    }

    /// <summary>
    /// Adds one key press and reports the longest matching entry. The caller suppresses the
    /// ending key when asked, then performs the returned backspacing and replay before starting
    /// the macro.
    /// </summary>
    public HotstringHit? Press(string typed, string endKey, IReadOnlyList<TriggerEntry> entries)
    {
        lock (_gate)
        {
            return PressLocked(typed, endKey, entries);
        }
    }

    private HotstringHit? PressLocked(string typed, string endKey,
        IReadOnlyList<TriggerEntry> entries)
    {
        if (typed.Length == 0)
        {
            return null;
        }

        var before = _text.ToString();
        var hit = Match(before, entries, typed, endKey);
        if (hit is not null)
        {
            _text.Clear();
            return hit;
        }

        _text.Append(typed);
        if (_text.Length > MaximumAbbreviationLength)
        {
            _text.Remove(0, _text.Length - MaximumAbbreviationLength);
        }

        var immediate = Match(_text.ToString(), entries, string.Empty, endKey);
        if (immediate is not null)
        {
            _text.Clear();
        }

        return immediate;
    }

    private static HotstringHit? Match(string text, IReadOnlyList<TriggerEntry> entries,
        string endingText, string endingKey)
    {
        foreach (var entry in entries
                     .Where(entry => entry.IsEnabled
                         && entry.Abbreviation.Length is > 0 and <= MaximumAbbreviationLength)
                     .OrderByDescending(entry => entry.Abbreviation.Length))
        {
            var abbreviation = entry.Abbreviation;
            var needsEnding = entry.RequireEndChar;
            if (needsEnding)
            {
                if (!HotstringCharacters.IsEndCharacter(endingText, entry.HotstringEndChars))
                {
                    continue;
                }
            }
            else if (endingText.Length > 0)
            {
                continue;
            }

            if (!text.EndsWith(abbreviation,
                    entry.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var before = text[..^abbreviation.Length];
            if (!entry.MatchInsideWord && before.Length > 0 && char.IsLetterOrDigit(before[^1]))
            {
                continue;
            }

            var erase = entry.EraseTyped ? abbreviation.Length : 0;
            var suppress = needsEnding && entry.EraseTyped;
            var replay = suppress && entry.KeepEndChar;
            return new HotstringHit(entry, endingText, endingKey, suppress, erase, replay);
        }

        return null;
    }
}

/// <summary>Turns a hook key and the held modifiers into the literal ASCII character it typed.</summary>
public static class HotstringText
{
    private static readonly HashSet<string> ShiftKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shift", "左Shift", "右Shift", "LShift", "RShift",
    };

    public static (string Text, string EndKey) Read(HookKey key, IEnumerable<string> held)
    {
        var name = KeyNames.Name(key);
        var shifted = held.Any(ShiftKeys.Contains);
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            var letter = char.ToUpperInvariant(name[0]);
            var capital = shifted ^ CapsLock();
            return (capital ? letter.ToString() : char.ToLowerInvariant(letter).ToString(), name);
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            if (!shifted)
            {
                return (name, name);
            }

            var shiftedDigit = name[0] switch
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
                _ => name,
            };
            return (shiftedDigit, name);
        }

        return name switch
        {
            "Space" or "空格" => (" ", name),
            "Enter" or "Return" or "回车" => ("\n", name),
            "Tab" => ("\t", name),
            "-" => (shifted ? "_" : "-", name),
            "=" => (shifted ? "+" : "=", name),
            "," => (shifted ? "<" : ",", name),
            "." => (shifted ? ">" : ".", name),
            "/" => (shifted ? "?" : "/", name),
            ";" => (shifted ? ":" : ";", name),
            "'" => (shifted ? "\"" : "'", name),
            "`" => (shifted ? "~" : "`", name),
            "[" => (shifted ? "{" : "[", name),
            "]" => (shifted ? "}" : "]", name),
            "\\" => (shifted ? "|" : "\\", name),
            _ => (string.Empty, name),
        };
    }

    private static bool CapsLock()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return (GetKeyState(0x14) & 1) != 0;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern short GetKeyState(int key);
}
