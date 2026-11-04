using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
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
/// One line of the structure: a piece of the formula as it is nested, with the depth it sits at.
/// The window draws them down the page with a guide per level, so the shape of a call is read by
/// the indentation rather than by counting brackets.
/// </summary>
public sealed record BlockRow(string Text, string Tone, int Depth, string Note = "", bool IsHole = false)
{
    /// <summary>One entry for every level above this row, which is one guide line on the page.</summary>
    public IReadOnlyList<int> Guides => Depth <= 0 ? [] : Enumerable.Range(0, Depth).ToArray();
}

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
    private bool _reads;

    public ValuePickerViewModel(string? seed, IReadOnlyList<VariableChoice>? variables, bool namesOnly,
        string? target = null)
    {
        NamesOnly = namesOnly;
        Target = target ?? string.Empty;
        Text = seed ?? string.Empty;

        // The wall keeps what a flat list cannot: the steps that read a name, and the way to one.
        // Its own search and filters are shown, because the variables panel is that wall.
        Wall = new VariableWallViewModel(variables) { ShowsDetails = false };
        Wall.Jumped += step => Jumped?.Invoke(step);
        Wall.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VariableWallViewModel.Looking))
            {
                // One material is being described at a time: picking a name lets go of the card
                // that was looked at before, and the other way round.
                if (Wall.Looking is not null)
                {
                    SelectedFunction = null;
                }

                OnPropertyChanged(nameof(HasChosenVariable));
                OnPropertyChanged(nameof(CanConfirm));
            }
        };

        Operators = ExpressionBuilderViewModel.BuildOperators();
        // A number is typed, and text has its own sign in the operator panel; what is left is the
        // three words a formula cannot be typed from memory as easily as it can be clicked.
        Values =
        [
            new ValueToken(Strings.Get("ExprBuilder.Value.True"), "true"),
            new ValueToken(Strings.Get("ExprBuilder.Value.False"), "false"),
            new ValueToken(Strings.Get("ExprBuilder.Value.Null"), "null"),
        ];

        _functions = [.. Language.Functions.Select(function => new ExpressionFunctionChoice(function))];
        FunctionGroups = BuildGroups();
        SelectedFunctionGroup = FunctionGroups[0];
        RefreshFunctions();
        CheckText();
    }

    /// <summary>Raised with what the cell should hold, or null when the window was left alone.</summary>
    public event Action<string?>? CloseRequested;

    /// <summary>Raised with a step somebody asked to be shown, so the editor behind can look.</summary>
    public event Action<MacroStep>? Jumped;

    public VariableWallViewModel Wall { get; }

    /// <summary>The signs a formula is built from, written as the engine reads them.</summary>
    public IReadOnlyList<ExpressionOperator> Operators { get; }

    /// <summary>The plain words that are worth a button rather than a memory.</summary>
    public IReadOnlyList<ValueToken> Values { get; }

    /// <summary>
    /// True where the cell only ever holds a name — the name a variable is written under, or the
    /// selector a step reads. A formula there would be stored as text and never read as one, so the
    /// window does not draw the formula or the two panels that build one.
    /// </summary>
    public bool NamesOnly { get; }

    public bool CanCompute => !NamesOnly;

    public string Title => NamesOnly ? Strings.Get("ValuePicker.Names") : Strings.Get("ValuePicker.Title");

    /// <summary>Which cell the window was opened for. Empty when nobody said.</summary>
    public string Target { get; }

    public string TargetLine => Target.Length > 0
        ? Strings.Format("ValuePicker.Target", Target)
        : string.Empty;

    public bool HasTarget => TargetLine.Length > 0;

    /// <summary>What the cell holds now, and what will go back into it.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>One line under the formula: whether it reads, or why it does not.</summary>
    [ObservableProperty]
    public partial string Note { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool NoteIsProblem { get; set; }

    /// <summary>The formula said in words, for a reader who does not follow the punctuation.</summary>
    [ObservableProperty]
    public partial string Saying { get; set; } = string.Empty;

    /// <summary>True when the formula reads, which is what lets it be handed back.</summary>
    public bool Reads => _reads;

    public bool HasSaying => Saying.Length > 0;

    /// <summary>What is typed into the function panel's search box.</summary>
    [ObservableProperty]
    public partial string FunctionQuery { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAllFunctions))]
    public partial ExpressionFunctionFilter? SelectedFunctionGroup { get; set; }

    public IReadOnlyList<ExpressionFunctionFilter> FunctionGroups { get; }

    public bool IsAllFunctions => SelectedFunctionGroup is { Group.Length: 0 };

    public ObservableCollection<ExpressionFunctionChoice> Functions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChosenFunction))]
    public partial ExpressionFunctionChoice? SelectedFunction { get; set; }

    public bool HasChosenFunction => SelectedFunction is not null;

    public bool HasChosenVariable => Wall.Looking is not null;

    public bool IsFunctionsEmpty => Functions.Count == 0;

    /// <summary>The formula as a tree of pieces, one row per piece, deepest last.</summary>
    public ObservableCollection<BlockRow> Blocks { get; } = [];

    public bool HasBlocks => Blocks.Count > 0;

    /// <summary>The piece the next click from a palette replaces, when one is chosen.</summary>
    [ObservableProperty]
    public partial BlockRow? SelectedBlock { get; set; }

    public bool CanConfirm => NamesOnly ? Wall.Looking is not null : _reads;

    partial void OnTextChanged(string value) => CheckText();

    partial void OnFunctionQueryChanged(string value) => RefreshFunctions();

    partial void OnSelectedFunctionGroupChanged(ExpressionFunctionFilter? value) => RefreshFunctions();

    partial void OnSelectedFunctionChanged(ExpressionFunctionChoice? value)
    {
        if (value is not null)
        {
            Wall.Looking = null;
        }
    }

    /// <summary>Narrows the function panel to the group and the word typed into it.</summary>
    public void RefreshFunctions()
    {
        var query = FunctionQuery.Trim();
        var group = SelectedFunctionGroup?.Group ?? string.Empty;

        Functions.Clear();
        foreach (var function in _functions)
        {
            if (group.Length > 0 && function.Group != group)
            {
                continue;
            }

            if (query.Length > 0 && !Matches(function, query))
            {
                continue;
            }

            Functions.Add(function);
        }

        OnPropertyChanged(nameof(IsFunctionsEmpty));

        // The row that is no longer in the list cannot stay chosen, and a list with nothing chosen
        // cannot say what a card means.
        if (SelectedFunction is not null && !Functions.Contains(SelectedFunction))
        {
            SelectedFunction = null;
        }
    }

    /// <summary>
    /// Puts a piece into the formula at the caret. One value is inserted where the person was
    /// typing rather than replacing the line: a formula is built a piece at a time.
    /// </summary>
    public int Insert(string token, int caret, int back = 0)
    {
        var at = Math.Clamp(caret, 0, Text.Length);
        Text = Text[..at] + token + Text[at..];
        return at + token.Length - back;
    }

    /// <summary>How far back the caret goes so a bracketed pair opens between its two halves.</summary>
    public static int BackIntoTokens(string token) => token switch
    {
        "()" or "[]" or "\"\"" => 1,
        _ => 0,
    };

    /// <summary>Hands back what the cell should hold: the line, or the one name it wanted.</summary>
    public void Confirm()
    {
        if (NamesOnly)
        {
            if (Wall.Looking is { } chosen)
            {
                CloseRequested?.Invoke(chosen.Token);
            }

            return;
        }

        if (_reads)
        {
            CloseRequested?.Invoke(Text.Trim());
        }
    }

    /// <summary>Hands back one name, for a cell that holds nothing else.</summary>
    public void Choose(string token) => CloseRequested?.Invoke(token);

    public void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>Why the formula does not read, or that it does. Empty is not a mistake yet.</summary>
    private void CheckText()
    {
        if (NamesOnly)
        {
            return;
        }

        var written = Text.Trim();
        if (written.Length == 0)
        {
            _reads = false;
            NoteIsProblem = false;
            Note = Strings.Get("ValuePicker.Note.Empty");
            Saying = string.Empty;
        }
        else if (Language.TryParse(written, out var parsed, out var error))
        {
            _reads = true;
            NoteIsProblem = false;
            Note = Strings.Get("ValuePicker.Note.Reads");
            Saying = ExpressionText.InChinese
                ? Strings.Get("ValuePicker.ReadsAs") + ExpressionPhrase.Say(parsed!, VariableCatalog.Spoken)
                : string.Empty;
        }
        else
        {
            _reads = false;
            NoteIsProblem = true;
            Note = error is null
                ? Strings.Get("ExprBuilder.CannotRead")
                : ExpressionText.Describe(error);
            Saying = string.Empty;
        }

        RebuildBlocks();

        OnPropertyChanged(nameof(Reads));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(HasSaying));
    }

    /// <summary>
    /// Lays the formula out as rows. An unreadable formula has no shape to show, and an empty one
    /// gets a single hole: the page says "this is where a value goes" rather than showing nothing.
    /// </summary>
    private void RebuildBlocks()
    {
        Blocks.Clear();

        if (NamesOnly)
        {
            return;
        }

        var written = Text.Trim();
        if (written.Length == 0)
        {
            Blocks.Add(new BlockRow(Strings.Get("ValuePicker.Hole"), "other", 0, IsHole: true));
        }
        else if (Language.TryParse(written, out var parsed, out _))
        {
            AddBlock(parsed!, 0);
        }

        OnPropertyChanged(nameof(HasBlocks));
    }

    private void AddBlock(ExpressionNode node, int depth)
    {
        switch (node)
        {
            case BinaryNode binary:
                Blocks.Add(new BlockRow(binary.Operator, "dates", depth));
                AddBlock(binary.Left, depth + 1);
                AddBlock(binary.Right, depth + 1);
                break;
            case UnaryNode unary:
                Blocks.Add(new BlockRow(unary.Operator, "records", depth));
                AddBlock(unary.Operand, depth + 1);
                break;
            case CallNode call:
                Blocks.Add(new BlockRow(call.Name, "text", depth, Strings.Get("ValuePicker.Block.Call")));
                foreach (var argument in call.Arguments)
                {
                    AddBlock(argument, depth + 1);
                }

                break;
            case ListNode list:
                Blocks.Add(new BlockRow("[ ]", "lists", depth, Strings.Get("ValuePicker.Block.List")));
                foreach (var item in list.Items)
                {
                    AddBlock(item, depth + 1);
                }

                break;
            case TextNode text:
                Blocks.Add(new BlockRow("\"" + text.Text + "\"", "text", depth));
                break;
            case VariableNode variable:
                Blocks.Add(new BlockRow("$" + variable.Name, "lists", depth));
                break;
            case NumberNode number:
                Blocks.Add(new BlockRow(number.Text, "maths", depth));
                break;
            case ConstantNode constant:
                Blocks.Add(new BlockRow(constant.Word, "other", depth));
                break;
            default:
                Blocks.Add(new BlockRow(node.ToString() ?? string.Empty, "other", depth));
                break;
        }
    }

    /// <summary>True when what is typed names this function, its call, or what it does.</summary>
    private static bool Matches(ExpressionFunctionChoice function, string query)
        => function.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Signature.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Heading.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.ChineseSignature.Contains(query, StringComparison.OrdinalIgnoreCase)
            || function.Hint.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>All of the language, then each group once, in the order the language states them.</summary>
    private IReadOnlyList<ExpressionFunctionFilter> BuildGroups()
    {
        var groups = new List<ExpressionFunctionFilter> { new(string.Empty, "ExprBuilder.FunctionsAll") };
        foreach (var function in _functions)
        {
            if (groups.All(filter => filter.Group != function.Group))
            {
                groups.Add(new ExpressionFunctionFilter(function.Group, $"Expr.Group.{function.Group}"));
            }
        }

        return groups;
    }
}
