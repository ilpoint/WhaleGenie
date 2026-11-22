using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>The literal keyboard for a typed-string trigger: caps write characters, not key names.</summary>
internal sealed record EditorCharacterPadSnapshot(int MaximumLength,
    IReadOnlyList<IReadOnlyList<EditorCharacterCapSnapshot>> Rows);

internal sealed record EditorCharacterCapSnapshot(string Normal, string Shifted, double Width,
    string Kind);

internal static class EditorCharacterPad
{
    public static EditorCharacterPadSnapshot Build() => new(HotstringCharacters.MaximumLength,
    [
        Row(Char("`", "~"), Char("1", "!"), Char("2", "@"), Char("3", "#"), Char("4", "$"),
            Char("5", "%"), Char("6", "^"), Char("7", "&"), Char("8", "*"), Char("9", "("),
            Char("0", ")"), Char("-", "_"), Char("=", "+"), Control("backspace", "Backspace", 2)),
        Row(Char("q", "Q"), Char("w", "W"), Char("e", "E"), Char("r", "R"), Char("t", "T"),
            Char("y", "Y"), Char("u", "U"), Char("i", "I"), Char("o", "O"), Char("p", "P"),
            Char("[", "{"), Char("]", "}"), Char("\\", "|")),
        Row(Char("a", "A"), Char("s", "S"), Char("d", "D"), Char("f", "F"), Char("g", "G"),
            Char("h", "H"), Char("j", "J"), Char("k", "K"), Char("l", "L"), Char(";", ":"),
            Char("'", "\""), Control("enter", "Enter", 2)),
        Row(Char("z", "Z"), Char("x", "X"), Char("c", "C"), Char("v", "V"), Char("b", "B"),
            Char("n", "N"), Char("m", "M"), Char(",", "<"), Char(".", ">"), Char("/", "?")),
        Row(Control("shift", "Shift", 2), Control("tab", "Tab", 1.4),
            Control("space", "Space", 5), Control("enter", "Enter", 1.4),
            Control("clear", "Clear", 1.2)),
    ]);

    private static IReadOnlyList<EditorCharacterCapSnapshot> Row(
        params EditorCharacterCapSnapshot[] caps) => caps;

    private static EditorCharacterCapSnapshot Char(string normal, string shifted)
        => new(normal, shifted, 1, "char");

    private static EditorCharacterCapSnapshot Control(string kind, string label, double width)
        => new(label, label, width, kind);
}
