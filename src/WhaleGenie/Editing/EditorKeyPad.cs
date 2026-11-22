using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Editing;

internal static class EditorKeyPad
{
    public static EditorKeyPadSnapshot Build(string mode)
    {
        var rows = new List<IReadOnlyList<EditorKeyCapSnapshot>>
        {
            Row(Cap("Esc", "Esc", 1.4), Cap("F1", "F1", 1), Cap("F2", "F2", 1),
                Cap("F3", "F3", 1), Cap("F4", "F4", 1), Cap("F5", "F5", 1),
                Cap("F6", "F6", 1), Cap("F7", "F7", 1), Cap("F8", "F8", 1),
                Cap("F9", "F9", 1), Cap("F10", "F10", 1), Cap("F11", "F11", 1),
                Cap("F12", "F12", 1)),
            Row(Cap("`", "`", 1), Digit(1), Digit(2), Digit(3), Digit(4), Digit(5),
                Digit(6), Digit(7), Digit(8), Digit(9), Digit(0), Cap("-", "-", 1),
                Cap("=", "=", 1), Cap("Backspace", "Backspace", 2)),
            Row(Cap("Tab", "Tab", 1.5), Letter("Q"), Letter("W"), Letter("E"), Letter("R"),
                Letter("T"), Letter("Y"), Letter("U"), Letter("I"), Letter("O"), Letter("P"),
                Cap("[", "[", 1), Cap("]", "]", 1), Cap("\\", "\\", 1.5)),
            Row(Cap("CapsLock", "CapsLock", 1.8), Letter("A"), Letter("S"), Letter("D"),
                Letter("F"), Letter("G"), Letter("H"), Letter("J"), Letter("K"), Letter("L"),
                Cap(";", ";", 1), Cap("'", "'", 1), Cap("Enter", "Enter", 2.2)),
            Row(Cap("左Shift", "左Shift", 2.3), Letter("Z"), Letter("X"), Letter("C"),
                Letter("V"), Letter("B"), Letter("N"), Letter("M"), Cap(",", ",", 1),
                Cap(".", ".", 1), Cap("/", "/", 1), Cap("右Shift", "右Shift", 2.3)),
            Row(Cap("左Ctrl", "左Ctrl", 1.5), Cap("左Win", "左Win", 1.2),
                Cap("左Alt", "左Alt", 1.2), Cap("Space", "Space", 5),
                Cap("右Alt", "右Alt", 1.2), Cap("右Win", "右Win", 1.2),
                Cap("Menu", "ContextMenu", 1.2), Cap("右Ctrl", "右Ctrl", 1.5)),
            Row(Cap("PrintScreen", "PrintScreen", 1.5), Cap("ScrollLock", "ScrollLock", 1.3),
                Cap("Pause", "Pause", 1.1), Cap("Insert", "Insert", 1.2),
                Cap("Home", "Home", 1.1), Cap("PageUp", "PageUp", 1.3),
                Cap("Delete", "Delete", 1.2), Cap("End", "End", 1.1),
                Cap("PageDown", "PageDown", 1.5)),
            Row(Cap("NumLock", "NumLock", 1.2), Cap("Num /", "NumPadDivide", 1),
                Cap("Num *", "NumPadMultiply", 1), Cap("Num -", "NumPadSubtract", 1),
                Cap("Num 7", "NumPad7", 1), Cap("Num 8", "NumPad8", 1),
                Cap("Num 9", "NumPad9", 1), Cap("Num +", "NumPadAdd", 1)),
            Row(Cap("Num 4", "NumPad4", 1), Cap("Num 5", "NumPad5", 1),
                Cap("Num 6", "NumPad6", 1), Cap("Num 1", "NumPad1", 1),
                Cap("Num 2", "NumPad2", 1), Cap("Num 3", "NumPad3", 1),
                Cap("Num Enter", "NumPadEnter", 1.4)),
            Row(Cap("Num 0", "NumPad0", 2), Cap("Num .", "NumPadDecimal", 1),
                Cap("Left", "Left", 1), Cap("Up", "Up", 1), Cap("Down", "Down", 1),
                Cap("Right", "Right", 1)),
            Row(Cap("F13", "F13", 1), Cap("F14", "F14", 1), Cap("F15", "F15", 1),
                Cap("F16", "F16", 1), Cap("F17", "F17", 1), Cap("F18", "F18", 1),
                Cap("F19", "F19", 1), Cap("F20", "F20", 1), Cap("F21", "F21", 1),
                Cap("F22", "F22", 1), Cap("F23", "F23", 1), Cap("F24", "F24", 1)),
            Row(Cap("MediaPrevious", "MediaPrevious", 1.6),
                Cap("MediaPlay", "MediaPlay", 1.4), Cap("MediaStop", "MediaStop", 1.2),
                Cap("MediaNext", "MediaNext", 1.4), Cap("MediaSelect", "MediaSelect", 1.4),
                Cap("MediaEject", "MediaEject", 1.3), Cap("VolumeMute", "VolumeMute", 1.3),
                Cap("VolumeDown", "VolumeDown", 1.4), Cap("VolumeUp", "VolumeUp", 1.2)),
            Row(Cap("BrowserBack", "BrowserBack", 1.5),
                Cap("BrowserForward", "BrowserForward", 1.6),
                Cap("BrowserRefresh", "BrowserRefresh", 1.5),
                Cap("BrowserStop", "BrowserStop", 1.4),
                Cap("BrowserSearch", "BrowserSearch", 1.5),
                Cap("BrowserHome", "BrowserHome", 1.4),
                Cap("BrowserFavorites", "BrowserFavorites", 1.7)),
            Row(Cap("AppBrowser", "AppBrowser", 1.4), Cap("AppCalculator", "AppCalculator", 1.6),
                Cap("AppMail", "AppMail", 1.2), Cap("Help", "Help", 1),
                Cap("Cancel", "Cancel", 1.1), Cap("Function", "Function", 1.2)),
        };

        AddRemaining(rows);
        return new EditorKeyPadSnapshot(string.Equals(mode, "keys", StringComparison.Ordinal)
            ? "keys" : "key", 0, rows);
    }

    private static void AddRemaining(List<IReadOnlyList<EditorKeyCapSnapshot>> rows)
    {
        var covered = rows.SelectMany(row => row)
            .Select(cap => KeyNames.ResolveCode(cap.Value))
            .Where(code => code is not null)
            .Select(code => code!.Value)
            .ToHashSet();
        var missing = KeyNames.Names
            .Where(name => KeyNames.ResolveCode(name) is { } code && !covered.Contains(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var at = 0; at < missing.Count; at += 8)
        {
            rows.Add(Row([.. missing.Skip(at).Take(8)
                .Select(name => Cap(name, name, 1))]));
        }
    }

    private static IReadOnlyList<EditorKeyCapSnapshot> Row(params EditorKeyCapSnapshot[] caps)
        => caps;

    private static EditorKeyCapSnapshot Digit(int digit)
        => Cap(digit.ToString(CultureInfo.InvariantCulture),
            digit.ToString(CultureInfo.InvariantCulture), 1);

    private static EditorKeyCapSnapshot Letter(string letter)
        => Cap(letter, letter, 1);

    private static EditorKeyCapSnapshot Cap(string label, string value, double width)
        => new(label, value, width);
}
