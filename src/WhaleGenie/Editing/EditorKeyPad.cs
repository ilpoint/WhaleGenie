using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Editing;

/// <summary>The keys drawn for a key field or a key sequence, in rows, with the value each writes.</summary>
internal sealed record EditorKeyPadSnapshot(string Mode, int MaxLength,
    IReadOnlyList<IReadOnlyList<EditorKeyCapSnapshot>> Rows);

internal sealed record EditorKeyCapSnapshot(string Label, string Value, double Width);

internal static class EditorKeyPad
{
    public static EditorKeyPadSnapshot Build(string mode)
    {
        if (mode == "text")
        {
            return new EditorKeyPadSnapshot(mode, 40,
            [
                Caps("1234567890", 1),
                Caps("qwertyuiop", 1),
                Caps("asdfghjkl;", 1),
                Caps("zxcvbnm,./", 1),
                [Cap("Space", " ", 4), Cap("Backspace", "\b", 2), Cap("Tab", "\t", 1.5),
                    Cap("Enter", "\n", 1.5)],
            ]);
        }

        return new EditorKeyPadSnapshot(mode, 0,
        [
            [Cap("Esc", "Esc", 1.5), Cap("F1", "F1", 1), Cap("F2", "F2", 1), Cap("F3", "F3", 1),
                Cap("F4", "F4", 1), Cap("F5", "F5", 1), Cap("F6", "F6", 1), Cap("F7", "F7", 1),
                Cap("F8", "F8", 1), Cap("F9", "F9", 1), Cap("F10", "F10", 1), Cap("F11", "F11", 1),
                Cap("F12", "F12", 1)],
            [Cap("`", "`", 1), Cap("1", "1", 1), Cap("2", "2", 1), Cap("3", "3", 1), Cap("4", "4", 1),
                Cap("5", "5", 1), Cap("6", "6", 1), Cap("7", "7", 1), Cap("8", "8", 1), Cap("9", "9", 1),
                Cap("0", "0", 1), Cap("-", "-", 1), Cap("=", "=", 1), Cap("Backspace", "Backspace", 2)],
            [Cap("Tab", "Tab", 1.5), Cap("Q", "Q", 1), Cap("W", "W", 1), Cap("E", "E", 1), Cap("R", "R", 1),
                Cap("T", "T", 1), Cap("Y", "Y", 1), Cap("U", "U", 1), Cap("I", "I", 1), Cap("O", "O", 1),
                Cap("P", "P", 1), Cap("[", "[", 1), Cap("]", "]", 1), Cap("\\", "\\", 1.5)],
            [Cap("Caps", "CapsLock", 1.8), Cap("A", "A", 1), Cap("S", "S", 1), Cap("D", "D", 1),
                Cap("F", "F", 1), Cap("G", "G", 1), Cap("H", "H", 1), Cap("J", "J", 1), Cap("K", "K", 1),
                Cap("L", "L", 1), Cap(";", ";", 1), Cap("'", "'", 1), Cap("Enter", "Enter", 2.2)],
            [Cap("Shift", "Shift", 2.3), Cap("Z", "Z", 1), Cap("X", "X", 1), Cap("C", "C", 1),
                Cap("V", "V", 1), Cap("B", "B", 1), Cap("N", "N", 1), Cap("M", "M", 1), Cap(",", ",", 1),
                Cap(".", ".", 1), Cap("/", "/", 1), Cap("Shift", "Shift", 2.3)],
            [Cap("Ctrl", "Ctrl", 1.5), Cap("Win", "Win", 1.2), Cap("Alt", "Alt", 1.2),
                Cap("Space", "Space", 5), Cap("Alt", "Alt", 1.2), Cap("Ctrl", "Ctrl", 1.5),
                Cap("Left", "Left", 1.2), Cap("Up", "Up", 1.2), Cap("Down", "Down", 1.2),
                Cap("Right", "Right", 1.2)],
        ]);
    }

    private static IReadOnlyList<EditorKeyCapSnapshot> Caps(string text, double width)
        => [.. text.Select(character => Cap(character.ToString().ToUpperInvariant(),
            character.ToString(), width))];

    private static EditorKeyCapSnapshot Cap(string label, string value, double width)
        => new(label, value, width);
}
