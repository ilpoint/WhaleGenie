using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

// The language itself, spelled out: the property below is the text being edited, which hides the
// static class of the same name inside this file.
using Language = WhaleGenie.Core.Expressions.Expression;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One of the plain values a formula can be made of: what the button says, what it inserts, and how
/// far back the caret goes so a pair of quotes opens between its two halves.
/// </summary>
public sealed record ValueToken(string Text, string Insert, int Back = 0);

/// <summary>
/// Backs the window that fills a cell. Composing a formula needs its materials in sight at once —
/// the signs, the names and the functions — so they stand in three panels on one page rather than
/// behind tabs: switching pages to reach the next piece of a formula is the cost this window is
/// built to remove. The formula itself is the one line everything writes into, and it is also the
/// line that can be typed in directly.
/// </summary>
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    private readonly List<ExpressionFunctionChoice> _functions;

    /// <summary>
    /// The function table keyed by the name a formula is written with, which is what the structure
    /// band asks while it reads: whether a name is a function, which family it belongs to, and how
    /// many places it needs.
    /// </summary>
    private readonly Dictionary<string, ExpressionFunction> _table;

    /// <summary>
    /// How the cell this window fills is read by the engine. The window checks a line with the
    /// same reader the field will use, so a line it calls readable cannot be stored as a mistake.
    /// </summary>
    private readonly ActionReadMode _readMode;

    /// <summary>
    /// True when a word of its own is a variable name rather than text, which is what a formula
    /// field says and what a line that may still be prose does not.
    /// </summary>
    private readonly bool _bareNames;

    /// <summary>
    /// Every name the cell could be reading, so a piece can say when nothing declares it. It is
    /// the same list the wall offers, which is why it is built here rather than guessed from text.
    /// </summary>
    private readonly HashSet<string> _knownNames = new(StringComparer.OrdinalIgnoreCase);

    private bool _reads;
    private int _caret;

    /// <summary>The shape the last check read the line into, which is what confirming hands back.</summary>
    private ExpressionNode? _parsed;
}
