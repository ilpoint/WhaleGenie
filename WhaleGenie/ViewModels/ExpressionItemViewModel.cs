using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>What one piece of a formula is: a place to fill in, a value, or something holding others.</summary>
public enum ExpressionItemKind
{
    /// <summary>Nothing has been put here yet.</summary>
    Hole,

    /// <summary>A number, as it was typed.</summary>
    Number,

    /// <summary>A piece of text, as it was typed, without the quotes it is written with.</summary>
    Text,

    /// <summary>A name the macro holds.</summary>
    Variable,

    /// <summary>True, false or null.</summary>
    Constant,

    /// <summary>Not, or a minus sign in front of one operand.</summary>
    Unary,

    /// <summary>Two operands and an operator between them.</summary>
    Binary,

    /// <summary>A function and its arguments.</summary>
    Call,

    /// <summary>A list written out.</summary>
    List,
}

/// <summary>
/// One piece of the formula as the structure page holds it: what it is, what is inside it, and what
/// the line for it looks like. The same object is both the piece and its line — the list of lines
/// is a flattening of the tree rather than a copy of it — so picking a line, dropping on a line and
/// changing the piece are all the same thing seen from different sides.
///
/// The tree is kept the way the editor needs it: pieces stay put while a formula is being built, so
/// the box a user is typing in is not thrown away and rebuilt under their hands.
/// </summary>
public sealed partial class ExpressionItemViewModel : ObservableObject
{
    /// <summary>How far one level of nesting moves a line to the right.</summary>
    private const double Step = 18;

    private readonly Action _changed;
    private string _literal = string.Empty;
    private bool _literalIsText;
    private int _depth;

    private ExpressionItemViewModel(ExpressionItemKind kind, Action changed)
    {
        Kind = kind;
        _changed = changed;
    }

    /// <summary>A place nothing has been put in yet.</summary>
    public static ExpressionItemViewModel Hole(Action changed) => new(ExpressionItemKind.Hole, changed);

    /// <summary>A name the macro holds, written without the dollar sign.</summary>
    public static ExpressionItemViewModel Variable(string name, Action changed)
        => new(ExpressionItemKind.Variable, changed) { Name = name };

    /// <summary>One of the words that stand for a value on its own.</summary>
    public static ExpressionItemViewModel Constant(string word, Action changed)
        => new(ExpressionItemKind.Constant, changed) { Name = word };

    /// <summary>A typed number or piece of text; an empty one is a place still to fill in.</summary>
    public static ExpressionItemViewModel Typed(string literal, bool asText, Action changed)
    {
        var item = new ExpressionItemViewModel(ExpressionItemKind.Hole, changed)
        {
            _literal = literal,
            _literalIsText = asText,
        };
        item.SettleKind();
        return item;
    }

    /// <summary>Not, or a minus sign, in front of one operand.</summary>
    public static ExpressionItemViewModel Unary(string operation, Action changed)
        => new(ExpressionItemKind.Unary, changed) { Name = operation };

    /// <summary>Two operands and the operator between them.</summary>
    public static ExpressionItemViewModel Binary(string operation, Action changed)
        => new(ExpressionItemKind.Binary, changed) { Name = operation };

    /// <summary>A function, with the arguments the table says it takes.</summary>
    public static ExpressionItemViewModel Call(ExpressionFunction function, Action changed)
        => new(ExpressionItemKind.Call, changed)
        {
            Name = function.Name,
            Heading = ExpressionText.Heading(function),
            Parameters = function.Parameters,
            MinArguments = function.MinArguments,
            MaxArguments = function.MaxArguments,
        };

    /// <summary>A list written out.</summary>
    public static ExpressionItemViewModel List(Action changed)
        => new(ExpressionItemKind.List, changed);

    /// <summary>What this piece is.</summary>
    public ExpressionItemKind Kind { get; private set; }

    /// <summary>The line this piece stands on, or the piece above it.</summary>
    public ExpressionItemViewModel? Parent { get; internal set; }

    /// <summary>What is inside this piece: the operands, the arguments or the items.</summary>
    public List<ExpressionItemViewModel> Children { get; } = [];

    /// <summary>The operator, the function or the word, for the pieces that are written as one.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// What a call is called on its line: the name in the reader's own language with the name the
    /// code calls it by beside it, which is the same thing the button it came from says.
    /// </summary>
    public string Heading { get; private set; } = string.Empty;

    /// <summary>The arguments a function takes, which is what its lines are named after.</summary>
    public IReadOnlyList<ExpressionParameter> Parameters { get; private set; } = [];

    /// <summary>How many arguments have to be there.</summary>
    public int MinArguments { get; private set; }

    /// <summary>How many may be there at most, counting <see cref="int.MaxValue"/> as no ceiling.</summary>
    public int MaxArguments { get; private set; }

    /// <summary>Which level of nesting this piece sits at, which is what moves its line across.</summary>
    public int Depth
    {
        get => _depth;
        internal set
        {
            if (_depth == value)
            {
                return;
            }

            _depth = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Indent));
        }
    }

    /// <summary>The name of the argument this line fills, for the lines under a function.</summary>
    public string Label { get; internal set; } = string.Empty;

    /// <summary>The name the signature uses, and the line saying what goes in it.</summary>
    public string LabelHint { get; internal set; } = string.Empty;

    /// <summary>True while this line is the one the next click from the palette goes into.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    /// <summary>True while something being dragged is over this line.</summary>
    [ObservableProperty]
    public partial bool IsDropTarget { get; set; }

    /// <summary>
    /// What was typed in this piece, exactly as it was typed: the digits of a number, or a piece of
    /// text without the quotes it is written with. Empty means nothing has been put here.
    /// </summary>
    public string Literal
    {
        get => _literal;
        set
        {
            if (string.Equals(_literal, value, StringComparison.Ordinal))
            {
                return;
            }

            _literal = value;
            SettleKind();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Token));
            _changed();
        }
    }

    /// <summary>
    /// True when what is typed here is to be read as text even if it looks like a number, which is
    /// what the "text" button sets: 5 is a number until somebody says otherwise.
    /// </summary>
    public bool ReadAsText
    {
        get => _literalIsText;
        set
        {
            if (_literalIsText == value)
            {
                return;
            }

            _literalIsText = value;
            SettleKind();
            OnPropertyChanged();
            OnPropertyChanged(nameof(Token));
            _changed();
        }
    }

    /// <summary>True for the pieces that are filled by typing in their own line.</summary>
    public bool IsTyped => Kind is ExpressionItemKind.Hole or ExpressionItemKind.Number
        or ExpressionItemKind.Text;

    /// <summary>True for the pieces written as a word on a line of their own.</summary>
    public bool ShowsToken => !IsTyped;

    /// <summary>What the line says, for the pieces that say it in words rather than by typing.</summary>
    public string Token => Kind switch
    {
        ExpressionItemKind.Variable => VariableCatalog.Display(Name, true),
        ExpressionItemKind.Constant => Name,
        ExpressionItemKind.Unary => Name == "not" ? "not" : "−x",
        ExpressionItemKind.Binary => Glyph(Name),
        ExpressionItemKind.Call => Heading,
        ExpressionItemKind.List => Strings.Get("ExprBuilder.Tree.List"),
        _ => string.Empty,
    };

    /// <summary>True while this line still has nothing in it.</summary>
    public bool IsHole => Kind == ExpressionItemKind.Hole;

    /// <summary>True while this line has a name of its own for whoever is reading it.</summary>
    public bool HasLabel => Label.Length > 0;

    /// <summary>How far this line is moved to the right, one step for each level above it.</summary>
    public Thickness Indent => new(Depth * Step, 0, 0, 0);

    /// <summary>True while another argument or item may be added under this piece.</summary>
    public bool CanAddChild => Kind switch
    {
        ExpressionItemKind.List => true,
        ExpressionItemKind.Call => MaxArguments == int.MaxValue || Children.Count < MaxArguments,
        _ => false,
    };

    /// <summary>True while one of the things under this piece may be taken away again.</summary>
    public bool CanDropChild => Kind switch
    {
        ExpressionItemKind.List => Children.Count > 0,
        ExpressionItemKind.Call => Children.Count > MinArguments,
        _ => false,
    };

    /// <summary>What the "add one" button does, in the reader's own language.</summary>
    public string AddHint => Strings.Get(Kind == ExpressionItemKind.List
        ? "ExprBuilder.Tree.AddItem"
        : "ExprBuilder.Tree.AddArgument");

    /// <summary>What the "take one away" button does.</summary>
    public string DropHint => Strings.Get(Kind == ExpressionItemKind.List
        ? "ExprBuilder.Tree.DropItem"
        : "ExprBuilder.Tree.DropArgument");

    /// <summary>What an empty place says, which is the one thing there is to say about it.</summary>
    public string HoleHint => Strings.Get("ExprBuilder.Tree.Hole");

    /// <summary>The piece as the engine's own tree, which is what turns it back into a formula.</summary>
    public ExpressionNode ToNode() => Kind switch
    {
        ExpressionItemKind.Variable => new VariableNode(Name),
        ExpressionItemKind.Constant => new ConstantNode(Name),
        ExpressionItemKind.Number => new NumberNode(Literal.Trim(), Number(Literal)),
        ExpressionItemKind.Text => new TextNode(Literal),
        ExpressionItemKind.Unary => new UnaryNode(Name, Child(0).ToNode()),
        ExpressionItemKind.Binary => new BinaryNode(Name, Child(0).ToNode(), Child(1).ToNode()),
        ExpressionItemKind.Call => new CallNode(Name, [.. Children.Select(child => child.ToNode())]),
        ExpressionItemKind.List => new ListNode([.. Children.Select(child => child.ToNode())]),
        _ => HoleNode.Instance,
    };

    /// <summary>Builds the piece an engine tree stands for, ready for the line it will be drawn on.</summary>
    public static ExpressionItemViewModel From(ExpressionNode node, Action changed)
    {
        switch (node)
        {
            case NumberNode number:
                return Typed(number.Text, false, changed);
            case TextNode text:
                return Typed(text.Text, true, changed);
            case VariableNode variable:
                return Variable(variable.Name, changed);
            case ConstantNode constant:
                return Constant(constant.Word, changed);
            case UnaryNode unary:
                return With(Unary(unary.Operator, changed), [unary.Operand], changed);
            case BinaryNode binary:
                return With(Binary(binary.Operator, changed), [binary.Left, binary.Right], changed);
            case CallNode call:
                return With(Call(Function(call.Name), changed),
                    [.. call.Arguments], changed);
            case ListNode list:
                return With(List(changed), [.. list.Items], changed);
            default:
                return Hole(changed);
        }
    }

    /// <summary>How many lines of this tree still have nothing in them.</summary>
    public int Holes()
        => (Kind == ExpressionItemKind.Hole ? 1 : 0) + Children.Sum(child => child.Holes());

    /// <summary>Turns a number that was typed into the value it stands for.</summary>
    private static double Number(string text)
        => double.TryParse(text.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    /// <summary>How an operator is written on the button and on its line, which is not how it is typed.</summary>
    private static string Glyph(string operation) => operation switch
    {
        "-" => "−",
        "*" => "×",
        "/" => "÷",
        "==" => "=",
        "!=" => "≠",
        ">=" => "≥",
        "<=" => "≤",
        _ => operation,
    };

    /// <summary>The function table entry a call was written with.</summary>
    private static ExpressionFunction Function(string name)
        => Expression.Functions.FirstOrDefault(
            function => string.Equals(function.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? new ExpressionFunction(name, name, string.Empty, string.Empty, [], 0, int.MaxValue);

    private static ExpressionItemViewModel With(ExpressionItemViewModel parent,
        IReadOnlyList<ExpressionNode> children, Action changed)
    {
        foreach (var child in children)
        {
            var item = From(child, changed);
            item.Parent = parent;
            parent.Children.Add(item);
        }

        return parent;
    }

    private ExpressionItemViewModel Child(int index)
        => index < Children.Count ? Children[index] : this;

    /// <summary>
    /// Settles whether what is typed here is a number, a piece of text, or nothing at all: empty is
    /// a place to fill, something that reads as a number is a number, and the rest is text.
    /// </summary>
    private void SettleKind()
    {
        if (_literal.Length == 0)
        {
            Kind = ExpressionItemKind.Hole;
        }
        else if (_literalIsText)
        {
            Kind = ExpressionItemKind.Text;
        }
        else
        {
            Kind = double.TryParse(_literal.Trim(), NumberStyles.Any,
                CultureInfo.InvariantCulture, out _)
                ? ExpressionItemKind.Number
                : ExpressionItemKind.Text;
        }

        OnPropertyChanged(nameof(IsTyped));
        OnPropertyChanged(nameof(ShowsToken));
        OnPropertyChanged(nameof(IsHole));
    }
}
