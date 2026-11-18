using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Views;

/// <summary>
/// One line of the multi-point colour search: an offset from the base colour and the colour there.
/// Kept apart from the window so the reading and writing of it can be checked without one.
/// </summary>
internal static class ColourPointLine
{
    /// <summary>How many colours one place may be satisfied by, its own included.</summary>
    public const int MostColours = 6;

    /// <summary>
    /// Reads "dx,dy,#RRGGBB" and writes it back the way the field wants it. An offset may be a sum
    /// of numbers, which is worked out here rather than in the head — the whole reason the line is
    /// edited in a box at all — or a formula kept as written for the run to work out.
    /// </summary>
    /// <remarks>
    /// A place may also carry the two columns the table beside it has and this box does not: whether
    /// it must be none of those colours, and a tolerance of its own. They are read and written back
    /// as they stand, so editing a line here never quietly clears something written elsewhere.
    /// </remarks>
    public static bool TryParse(string? text, out string entry)
    {
        entry = string.Empty;
        var parts = ColourPointText.Split(text ?? string.Empty);
        if (parts.Count is not (3 or 5)
            || !TryOffset(parts[0], out var dx)
            || !TryOffset(parts[1], out var dy)
            || !TryColours(parts[2], out var colours))
        {
            return false;
        }

        var extra = string.Empty;
        if (parts.Count == 5)
        {
            var mustNot = parts[3].Trim();
            var tolerance = parts[4].Trim();
            if (!IsFlag(mustNot)
                || (tolerance.Length > 0 && !TryOffset(tolerance, out _)))
            {
                return false;
            }

            extra = $",{(mustNot is "1" or "true" ? "1" : mustNot is "0" or "false" ? "0" : string.Empty)},{tolerance}";
        }

        entry = $"{dx},{dy},{string.Join('|', colours)}{extra}";
        return true;
    }

    /// <summary>Whether a column says yes, no, or nothing at all.</summary>
    private static bool IsFlag(string text)
        => text.Length == 0 || text is "0" or "1" or "true" or "false";

    /// <summary>
    /// The offset and the colours of a line, without the two columns behind them: what the list and
    /// the picture show is a place and what has to be there, not how strictly.
    /// </summary>
    public static string Core(string line)
    {
        var parts = ColourPointText.Split(line);
        return parts.Count < 3
            ? line
            : string.Join(',', parts[0], parts[1], parts[2]);
    }

    /// <summary>The colours one place may be satisfied by, as written.</summary>
    public static IReadOnlyList<string> Colours(string line)
    {
        var parts = ColourPointText.Split(line);
        return parts.Count < 3
            ? []
            : [.. parts[2].Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    /// <summary>Whether a line says its place must be none of those colours.</summary>
    public static bool MustNot(string line)
        => ColourPointText.Split(line) is { Count: > 3 } parts && parts[3].Trim() is "1" or "true";

    /// <summary>The tolerance this one place was given, or nothing when it uses the search's own.</summary>
    public static string Tolerance(string line)
        => ColourPointText.Split(line) is { Count: > 4 } parts ? parts[4].Trim() : string.Empty;

    /// <summary>
    /// The line with a new offset and a new colour, and the two columns behind them left exactly as
    /// they were: moving a place does not make it less strict.
    /// </summary>
    public static string WithCore(string line, string dx, string dy, string colour)
    {
        var parts = ColourPointText.Split(line);
        var head = string.Join(',', dx, dy, colour);
        if (parts.Count <= 3)
        {
            return head;
        }

        return $"{head},{parts[3]},{(parts.Count > 4 ? parts[4] : string.Empty)}";
    }

    /// <summary>The line with another set of colours, keeping its offsets and the columns behind them.</summary>
    public static string WithColours(string line, IReadOnlyList<string> colours)
    {
        var parts = ColourPointText.Split(line);
        var dx = parts.Count > 0 ? parts[0] : string.Empty;
        var dy = parts.Count > 1 ? parts[1] : string.Empty;
        return WithCore(line, dx, dy, string.Join('|', colours));
    }

    /// <summary>
    /// One offset: a number, a sum of them, or a formula. A sum is worked out now; a formula is left
    /// standing, because what it comes to is known when the macro runs rather than while it is being
    /// written.
    /// </summary>
    public static bool TryOffset(string? token, out string written)
    {
        var text = (token ?? string.Empty).Trim();
        written = text;
        if (text.Length == 0)
        {
            return false;
        }

        if (Sum.TryEvaluate(text, out var value))
        {
            written = value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return Expression.TryParse(text, out _, out _);
    }

    /// <summary>The colour: written out, or a formula that holds one.</summary>
    public static bool TryColour(string? token, out string written)
    {
        var text = (token ?? string.Empty).Trim();
        written = text;
        if (TryColourText(text, out var colour))
        {
            written = colour;
            return true;
        }

        // A colour named by a variable, or worked out by a function, is read the same way an offset
        // is: it is kept as written and the run works it out.
        return text.Contains('$', StringComparison.Ordinal)
            && Expression.TryParse(text, out _, out _);
    }

    /// <summary>
    /// The colours of one place, one after another with a bar between them: a button drawn light in
    /// one state and dark in another is one place that may be either, not two patterns. Each colour
    /// is read exactly as a single one is.
    /// </summary>
    public static bool TryColours(string? text, out IReadOnlyList<string> colours)
    {
        colours = [];
        var tokens = (text ?? string.Empty).Split('|',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var written = new List<string>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!TryColour(token, out var colour))
            {
                return false;
            }

            written.Add(colour);
        }

        colours = written;
        return true;
    }

    /// <summary>Whether the text is one colour written out, either case accepted.</summary>
    public static bool TryColourText(string text, out string colour)
    {
        colour = string.Empty;
        if (text.Length != 7 || text[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < text.Length; index++)
        {
            if (!Uri.IsHexDigit(text[index]))
            {
                return false;
            }
        }

        colour = text.ToUpperInvariant();
        return true;
    }

    /// <summary>Whether a number is one this line can use: written out, or written as a sum.</summary>
    public static bool TryNumber(string text, out int value) => Sum.TryEvaluate(text, out value);

    /// <summary>
    /// Whole numbers with + - * / and brackets. Deliberately small: it is here so a field can say
    /// "12+3" instead of making somebody work it out, not to be a calculator.
    /// </summary>
    private static class Sum
    {
        public static bool TryEvaluate(string text, out int value)
        {
            var at = 0;
            value = 0;
            if (!Term(text, ref at, out var result))
            {
                return false;
            }

            Space(text, ref at);
            if (at != text.Length)
            {
                return false;
            }

            value = result;
            return true;
        }

        private static bool Term(string text, ref int at, out int value)
        {
            if (!Factor(text, ref at, out value))
            {
                return false;
            }

            while (true)
            {
                Space(text, ref at);
                if (at >= text.Length || text[at] is not ('+' or '-'))
                {
                    return true;
                }

                var plus = text[at] == '+';
                at++;
                if (!Factor(text, ref at, out var right))
                {
                    return false;
                }

                value = plus ? value + right : value - right;
            }
        }

        private static bool Factor(string text, ref int at, out int value)
        {
            if (!Atom(text, ref at, out value))
            {
                return false;
            }

            while (true)
            {
                Space(text, ref at);
                if (at >= text.Length || text[at] is not ('*' or '/'))
                {
                    return true;
                }

                var times = text[at] == '*';
                at++;
                if (!Atom(text, ref at, out var right))
                {
                    return false;
                }

                if (!times && right == 0)
                {
                    return false;
                }

                value = times ? value * right : value / right;
            }
        }

        private static bool Atom(string text, ref int at, out int value)
        {
            value = 0;
            Space(text, ref at);
            if (at >= text.Length)
            {
                return false;
            }

            var negative = false;
            while (at < text.Length && text[at] is '-' or '+')
            {
                negative ^= text[at] == '-';
                at++;
            }

            // A bracket after a sign is read as a bracket: -(1+1) is one number, not a sign looking
            // for a digit that is not there.
            if (at < text.Length && text[at] == '(')
            {
                at++;
                if (!Term(text, ref at, out var bracketed))
                {
                    return false;
                }

                Space(text, ref at);
                if (at >= text.Length || text[at] != ')')
                {
                    return false;
                }

                at++;
                value = negative ? -bracketed : bracketed;
                return true;
            }

            var start = at;
            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            if (start == at
                || !int.TryParse(text.AsSpan(start, at - start), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var number))
            {
                return false;
            }

            value = negative ? -number : number;
            return true;
        }

        private static void Space(string text, ref int at)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at]))
            {
                at++;
            }
        }
    }
}

/// <summary>
/// Edits one line of a pattern: the offset from the base colour, and the colour there. Typed rather
/// than clicked, because a line that came out of a click is often one pixel or two off — and any of
/// the three may be a formula instead of a number, which is what the button beside each box builds.
/// </summary>
public partial class PickEntryWindow : AppWindow
{
    /// <summary>What the three boxes may name: the variables known to the step being written.</summary>
    private readonly IReadOnlyList<VariableChoice> _variables;

    /// <summary>
    /// The two columns this box does not edit — whether the place must be none of the colours, and a
    /// tolerance of its own — kept as they were read so that confirming does not clear them.
    /// </summary>
    private string _extra = string.Empty;

    public PickEntryWindow()
    {
        InitializeComponent();
        _variables = [];
    }

    /// <param name="entry">The line to change; empty when a new one is being added.</param>
    /// <param name="variables">The names a box may use.</param>
    /// <param name="adding">True when the line is new, which is only a different word on the title.</param>
    public PickEntryWindow(string entry, IReadOnlyList<VariableChoice> variables, bool adding)
    {
        InitializeComponent();
        WindowChrome.Attach(this);

        _variables = variables;
        Title = Strings.Get(adding ? "LookPick.AddTitle" : "LookPick.EntryTitle");
        var parts = ColourPointText.Split(entry ?? string.Empty);
        if (parts.Count >= 3)
        {
            Box("OffsetX").Text = parts[0];
            Box("OffsetY").Text = parts[1];
            Box("Colour").Text = parts[2];
            _extra = parts.Count > 3
                ? $",{parts[3]},{(parts.Count > 4 ? parts[4] : string.Empty)}"
                : string.Empty;
        }

        foreach (var name in new[] { "OffsetX", "OffsetY", "Colour" })
        {
            Box(name).TextChanged += (_, _) => Preview();
            if (VariableTip() is { } tip)
            {
                ToolTip.SetTip(Box(name), tip);
            }
        }

        Preview();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Dispatcher.UIThread.Post(() => Box("OffsetX").Focus());
    }

    /// <summary>Edits one line of a pattern, or reports null when the user changed their mind.</summary>
    public static async Task<string?> EditAsync(Window owner, string entry,
        IReadOnlyList<VariableChoice> variables)
        => await new PickEntryWindow(entry, variables, false).ShowDialogOver<string?>(owner);

    /// <summary>Writes a new line, or reports null when the user changed their mind.</summary>
    public static async Task<string?> AddAsync(Window owner, string entry,
        IReadOnlyList<VariableChoice> variables)
        => await new PickEntryWindow(entry, variables, true).ShowDialogOver<string?>(owner);

    /// <summary>
    /// 三格没有表达式按钮了：这里是手填一个点，写进去的是数字和颜色。算式与变量仍然可以直接打，
    /// 所以把能用的名字写在提示里，省得有人去翻别处找变量叫什么。
    /// </summary>
    private string? VariableTip()
        => _variables.Count == 0
            ? null
            : Strings.Format("LookPick.EntryVariableTip",
                string.Join("、", _variables.Take(6).Select(choice => "$" + choice.Name)));

    private TextBox Box(string name) => this.FindControl<TextBox>(name)!;

    /// <summary>Shows the colour being named, so a typed one can be seen before it is taken.</summary>
    private void Preview()
    {
        if (this.FindControl<Border>("Swatch") is not { } swatch)
        {
            return;
        }

        // 一处可以写几种颜色：色块显示第一种写得出来的，写的是算式就干脆不画色块。
        var colours = ColourPointLine.TryColours(Box("Colour").Text, out var read) ? read : [];
        swatch.Background = colours.Count > 0
            && ColourPointLine.TryColourText(colours[0], out var colour)
                ? new SolidColorBrush(Color.Parse(colour))
                : null;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        var line = string.Join(',', Box("OffsetX").Text ?? string.Empty,
            Box("OffsetY").Text ?? string.Empty, Box("Colour").Text ?? string.Empty) + _extra;
        if (ColourPointLine.TryParse(line, out var entry))
        {
            Close(entry);
            return;
        }

        if (this.FindControl<TextBlock>("Problem") is { } problem)
        {
            problem.Text = Strings.Get("LookPick.EntryBad");
            problem.IsVisible = true;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                Close(null);
                break;

            case Key.Enter:
                e.Handled = true;
                OnConfirm(sender, new RoutedEventArgs());
                break;

            default:
                break;
        }
    }
}
