using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// One piece of an expression: the shape the editor shows and fills in, and the shape the
/// evaluator runs. Reading the text into a tree before thinking about values is what lets the
/// editor show a formula as boxes, so the reader itself stays free of anything the interface
/// needs.
/// </summary>
public abstract record ExpressionNode;

/// <summary>A number, kept as it was written so the text can be handed back unchanged.</summary>
public sealed record NumberNode(string Text, double Value) : ExpressionNode;

/// <summary>A piece of text, held unescaped; the writer adds the quotes and the backslashes.</summary>
public sealed record TextNode(string Text) : ExpressionNode;

/// <summary>A name the macro holds, written without the dollar sign that reads it.</summary>
public sealed record VariableNode(string Name) : ExpressionNode;

/// <summary>One of the words that stand for a value on its own: true, false or null.</summary>
public sealed record ConstantNode(string Word) : ExpressionNode;

/// <summary>One operand and the operator in front of it, written <c>not</c> or <c>-</c>.</summary>
public sealed record UnaryNode(string Operator, ExpressionNode Operand) : ExpressionNode;

/// <summary>Two operands and the operator between them.</summary>
public sealed record BinaryNode(string Operator, ExpressionNode Left, ExpressionNode Right)
    : ExpressionNode;

/// <summary>A function and its arguments, in the order they are written.</summary>
public sealed record CallNode(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;

/// <summary>A list written out, such as <c>[1, 2, 3]</c>.</summary>
public sealed record ListNode(IReadOnlyList<ExpressionNode> Items) : ExpressionNode;

/// <summary>
/// A place in the tree nothing has been put in yet. Reading a formula never produces one — text
/// with a space in it does not read at all — so this is the editor's own shape: it stands for a box
/// the user has not filled in, and the writer leaves it as a placeholder.
/// </summary>
public sealed record HoleNode : ExpressionNode
{
    /// <summary>The one empty place, since there is nothing to tell two of them apart.</summary>
    public static HoleNode Instance { get; } = new();
}

/// <summary>Turns expression text into the tree the editor edits and the evaluator runs.</summary>
internal static class ExpressionReader
{
    public static ExpressionNode Read(string source)
    {
        var parser = new Parser(Tokenizer.Read(source));
        var node = parser.ReadOr();
        if (parser.Current.Kind != TokenKind.End)
        {
            throw ExpressionException.Syntax($"unexpected \"{parser.Current.Text}\"");
        }

        return node;
    }
}

file enum TokenKind
{
    Number,
    Text,
    Variable,
    Name,
    Symbol,
    End,
}

file readonly record struct Token(TokenKind Kind, string Text, double Number);

/// <summary>Splits expression text into the tokens the reader walks over.</summary>
file static class Tokenizer
{
    public static List<Token> Read(string source)
    {
        var tokens = new List<Token>();
        var index = 0;

        while (index < source.Length)
        {
            var character = source[index];
            if (char.IsWhiteSpace(character))
            {
                index++;
                continue;
            }

            if (char.IsDigit(character)
                || (character == '.' && index + 1 < source.Length && char.IsDigit(source[index + 1])))
            {
                var start = index;
                while (index < source.Length && (char.IsDigit(source[index]) || source[index] == '.'))
                {
                    index++;
                }

                var text = source[start..index];
                if (!double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                {
                    throw ExpressionException.Syntax($"\"{text}\" is not a number");
                }

                tokens.Add(new Token(TokenKind.Number, text, number));
                continue;
            }

            if (character is '"' or '\'')
            {
                index = ReadText(source, index, character, tokens);
                continue;
            }

            if (character == '$')
            {
                index++;
                var start = index;
                while (index < source.Length && IsNameCharacter(source[index]))
                {
                    index++;
                }

                if (index == start)
                {
                    throw ExpressionException.Syntax("a variable name is missing after \"$\"");
                }

                tokens.Add(new Token(TokenKind.Variable, source[start..index], 0));
                continue;
            }

            if (char.IsLetter(character) || character == '_')
            {
                var start = index;
                while (index < source.Length && (char.IsLetterOrDigit(source[index]) || source[index] == '_'))
                {
                    index++;
                }

                tokens.Add(new Token(TokenKind.Name, source[start..index], 0));
                continue;
            }

            var pair = index + 1 < source.Length ? source.Substring(index, 2) : string.Empty;
            if (pair is "==" or "!=" or "<=" or ">=" or "&&" or "||")
            {
                tokens.Add(new Token(TokenKind.Symbol, pair, 0));
                index += 2;
                continue;
            }

            if ("+-*/%(),[]<>!=".Contains(character))
            {
                if (character == '=')
                {
                    throw ExpressionException.Syntax("\"=\" on its own; write \"==\" to compare");
                }

                tokens.Add(new Token(TokenKind.Symbol, character.ToString(), 0));
                index++;
                continue;
            }

            throw ExpressionException.Syntax($"unexpected character \"{character}\"");
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, 0));
        return tokens;
    }

    private static bool IsNameCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '.';

    private static int ReadText(string source, int index, char quote, List<Token> tokens)
    {
        var builder = new StringBuilder();
        index++;

        while (index < source.Length)
        {
            var current = source[index];
            if (current == '\\' && index + 1 < source.Length)
            {
                builder.Append(source[index + 1] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    var escaped => escaped,
                });
                index += 2;
                continue;
            }

            if (current == quote)
            {
                tokens.Add(new Token(TokenKind.Text, builder.ToString(), 0));
                return index + 1;
            }

            builder.Append(current);
            index++;
        }

        throw ExpressionException.Syntax("a closing quote is missing");
    }
}

/// <summary>
/// Walks the tokens into a tree. Written the way the language reads: <c>or</c> opens the loosest
/// level and each step down binds more tightly, so a formula written without brackets still comes
/// out as the tree the writer would draw.
/// </summary>
file sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _index;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public Token Current => _tokens[_index];

    public ExpressionNode ReadOr()
    {
        var left = ReadAnd();
        while (TakeWord("or") || TakeSymbol("||"))
        {
            left = new BinaryNode("or", left, ReadAnd());
        }

        return left;
    }

    private ExpressionNode ReadAnd()
    {
        var left = ReadComparison();
        while (TakeWord("and") || TakeSymbol("&&"))
        {
            left = new BinaryNode("and", left, ReadComparison());
        }

        return left;
    }

    private ExpressionNode ReadComparison()
    {
        var left = ReadAdditive();
        while (Current.Kind == TokenKind.Symbol
            && Current.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
        {
            left = new BinaryNode(Take().Text, left, ReadAdditive());
        }

        return left;
    }

    private ExpressionNode ReadAdditive()
    {
        var left = ReadMultiplicative();
        while (Current.Kind == TokenKind.Symbol && Current.Text is "+" or "-")
        {
            left = new BinaryNode(Take().Text, left, ReadMultiplicative());
        }

        return left;
    }

    private ExpressionNode ReadMultiplicative()
    {
        var left = ReadUnary();
        while (Current.Kind == TokenKind.Symbol && Current.Text is "*" or "/" or "%")
        {
            left = new BinaryNode(Take().Text, left, ReadUnary());
        }

        return left;
    }

    private ExpressionNode ReadUnary()
    {
        if (TakeSymbol("-"))
        {
            return new UnaryNode("-", ReadUnary());
        }

        if (TakeSymbol("!") || TakeWord("not"))
        {
            return new UnaryNode("not", ReadUnary());
        }

        return ReadPrimary();
    }

    private ExpressionNode ReadPrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                _index++;
                return new NumberNode(token.Text, token.Number);

            case TokenKind.Text:
                _index++;
                return new TextNode(token.Text);

            case TokenKind.Variable:
                _index++;
                return new VariableNode(token.Text);

            case TokenKind.Name:
                _index++;
                return TakeSymbol("(")
                    ? Call(token.Text, ReadArguments())
                    : Constant(token.Text);

            case TokenKind.Symbol when token.Text == "(":
                _index++;
                var grouped = ReadOr();
                if (!TakeSymbol(")"))
                {
                    throw ExpressionException.Syntax("a \")\" is missing");
                }

                return grouped;

            case TokenKind.Symbol when token.Text == "[":
                _index++;
                var items = new List<ExpressionNode>();
                if (!TakeSymbol("]"))
                {
                    do
                    {
                        items.Add(ReadOr());
                    }
                    while (TakeSymbol(","));

                    if (!TakeSymbol("]"))
                    {
                        throw ExpressionException.Syntax("a \"]\" is missing");
                    }
                }

                return new ListNode(items);

            default:
                throw ExpressionException.Syntax(token.Kind == TokenKind.End
                    ? "the expression ends too early"
                    : $"unexpected \"{token.Text}\"");
        }
    }

    private List<ExpressionNode> ReadArguments()
    {
        var arguments = new List<ExpressionNode>();
        if (TakeSymbol(")"))
        {
            return arguments;
        }

        do
        {
            arguments.Add(ReadOr());
        }
        while (TakeSymbol(","));

        if (!TakeSymbol(")"))
        {
            throw ExpressionException.Syntax("a \")\" is missing");
        }

        return arguments;
    }

    private static ExpressionNode Call(string name, List<ExpressionNode> arguments)
    {
        if (!FunctionLibrary.TryGet(name, out var entry))
        {
            throw ExpressionException.UnknownFunction(name);
        }

        if (arguments.Count < entry.MinArguments || arguments.Count > entry.MaxArguments)
        {
            throw ExpressionException.ArgumentCount(entry.Signature);
        }

        // A function is kept as it was written, so a name typed in the reader's own capitalisation
        // comes back the same way rather than being rewritten to the table's spelling.
        return new CallNode(name, arguments);
    }

    private static ExpressionNode Constant(string word) => word.ToLowerInvariant() switch
    {
        "true" => new ConstantNode("true"),
        "false" => new ConstantNode("false"),
        "null" => new ConstantNode("null"),
        _ => throw ExpressionException.Syntax($"unknown word \"{word}\""),
    };

    private Token Take()
    {
        var token = _tokens[_index];
        _index++;
        return token;
    }

    private bool TakeSymbol(string symbol)
    {
        if (Current.Kind != TokenKind.Symbol || Current.Text != symbol)
        {
            return false;
        }

        _index++;
        return true;
    }

    private bool TakeWord(string word)
    {
        if (Current.Kind != TokenKind.Name
            || !string.Equals(Current.Text, word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        _index++;
        return true;
    }
}

/// <summary>
/// Writes a tree back out as text. Brackets are put in only where the tree would read differently
/// without them, which is what lets the editor own the shape of a formula: the user moves boxes
/// around and this decides where the brackets land.
/// </summary>
internal static class ExpressionWriter
{
    /// <summary>What binds loosest, counting up to the pieces that bind nothing at all.</summary>
    private const int Or = 1;

    private const int And = 2;

    private const int Comparison = 3;

    private const int Additive = 4;

    private const int Multiplicative = 5;

    private const int Prefix = 6;

    private const int Atom = 7;

    public static string Write(ExpressionNode node, string hole)
        => Piece(node, hole, out _);

    private static string Piece(ExpressionNode node, string hole, out int precedence)
    {
        switch (node)
        {
            case NumberNode number:
                precedence = Atom;
                return number.Text;

            case TextNode text:
                precedence = Atom;
                return Quoted(text.Text);

            case VariableNode variable:
                precedence = Atom;
                return "$" + variable.Name;

            case ConstantNode constant:
                precedence = Atom;
                return constant.Word;

            case ListNode list:
                precedence = Atom;
                return "[" + string.Join(", ", list.Items.Select(item => Piece(item, hole, out _))) + "]";

            case CallNode call:
                precedence = Atom;
                return call.Name + "("
                    + string.Join(", ", call.Arguments.Select(argument => Piece(argument, hole, out _)))
                    + ")";

            case UnaryNode unary:
                precedence = Prefix;
                var operand = Piece(unary.Operand, hole, out var operandPrecedence);
                return (unary.Operator == "not" ? "not " : "-")
                    + Loose(operand, operandPrecedence, Prefix, false, false);

            case BinaryNode binary:
                precedence = Binds(binary.Operator);
                var left = Piece(binary.Left, hole, out var leftPrecedence);
                var right = Piece(binary.Right, hole, out var rightPrecedence);
                return Loose(left, leftPrecedence, precedence, false, false)
                    + " " + binary.Operator + " "
                    + Loose(right, rightPrecedence, precedence, true, !ReadsTheSame(binary.Operator));

            case HoleNode:
                precedence = Atom;
                return hole;

            default:
                throw new InvalidOperationException($"no way to write {node.GetType().Name}");
        }
    }

    /// <summary>How tightly an operator holds its operands, which is the order the language reads them in.</summary>
    private static int Binds(string operation) => operation switch
    {
        "or" => Or,
        "and" => And,
        "==" or "!=" or "<" or "<=" or ">" or ">=" => Comparison,
        "+" or "-" => Additive,
        _ => Multiplicative,
    };

    /// <summary>
    /// True for the operators whose right operand needs no brackets when it binds exactly as
    /// tightly: what they do to their two operands in turn comes out the same either way. The rest
    /// — subtraction, multiplication, division, the remainder and the comparisons — read
    /// differently nested the other way round, so their right operand keeps its brackets.
    /// </summary>
    private static bool ReadsTheSame(string operation) => operation is "+" or "and" or "or";

    private static string Loose(string text, int child, int parent, bool right, bool tight)
        => child < parent || (tight && child == parent && right) ? "(" + text + ")" : text;

    /// <summary>The text with the quotes the language reads it back through.</summary>
    private static string Quoted(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}

/// <summary>
/// Turns a tree into the thunk that works the value out. Thunks rather than values is what keeps
/// <c>and</c>, <c>or</c> and <c>if</c> lazy: the branch that is not taken never runs.
/// </summary>
internal static class ExpressionCompiler
{
    public static Func<Value> Compile(ExpressionNode node, IVariableResolver variables)
        => node switch
        {
            NumberNode number => () => Value.FromNumber(number.Value),
            TextNode text => () => Value.FromText(text.Text),
            ConstantNode constant => Compiled(constant.Word),
            VariableNode variable => () => Resolve(variables, variable.Name),
            ListNode list => Compiled(list, variables),
            CallNode call => Compiled(call, variables),
            UnaryNode unary => Compiled(unary, variables),
            BinaryNode binary => Compiled(binary, variables),
            _ => throw ExpressionException.Syntax("a place in the formula is still empty"),
        };

    private static Func<Value> Compiled(string word) => word switch
    {
        "true" => () => Value.FromBool(true),
        "false" => () => Value.FromBool(false),
        _ => () => Value.Null,
    };

    private static Func<Value> Compiled(ListNode list, IVariableResolver variables)
    {
        var items = list.Items.Select(item => Compile(item, variables)).ToList();
        return () => Value.FromList(items.Select(item => item()));
    }

    private static Func<Value> Compiled(CallNode call, IVariableResolver variables)
    {
        if (!FunctionLibrary.TryGet(call.Name, out var entry))
        {
            throw ExpressionException.UnknownFunction(call.Name);
        }

        if (call.Arguments.Count < entry.MinArguments || call.Arguments.Count > entry.MaxArguments)
        {
            throw ExpressionException.ArgumentCount(entry.Signature);
        }

        var arguments = call.Arguments.Select(argument => Compile(argument, variables)).ToList();
        return () => entry.Body(arguments);
    }

    private static Func<Value> Compiled(UnaryNode unary, IVariableResolver variables)
    {
        var operand = Compile(unary.Operand, variables);
        return unary.Operator == "not"
            ? () => Value.FromBool(!operand().AsBool())
            : () => Value.FromNumber(-operand().AsNumber());
    }

    private static Func<Value> Compiled(BinaryNode binary, IVariableResolver variables)
    {
        var left = Compile(binary.Left, variables);
        var right = Compile(binary.Right, variables);

        switch (binary.Operator)
        {
            case "or":
                return () => Value.FromBool(left().AsBool() || right().AsBool());
            case "and":
                return () => Value.FromBool(left().AsBool() && right().AsBool());
            case "+":
                return () => Add(left(), right());
            case "-":
                return () => Value.FromNumber(left().AsNumber() - right().AsNumber());
            case "*":
                return () => Multiply("*", left(), right());
            case "/":
                return () => Multiply("/", left(), right());
            case "%":
                return () => Multiply("%", left(), right());
            default:
                return () => Compare(binary.Operator, left(), right());
        }
    }

    private static Value Resolve(IVariableResolver variables, string name)
        => variables.TryGet(name, out var value)
            ? value
            : throw ExpressionException.UnknownVariable(name);

    private static Value Add(Value left, Value right)
    {
        if (left.Kind is ValueKind.Text or ValueKind.List or ValueKind.Record
            || right.Kind is ValueKind.Text or ValueKind.List or ValueKind.Record)
        {
            return Value.FromText(left.AsText() + right.AsText());
        }

        return Value.FromNumber(left.AsNumber() + right.AsNumber());
    }

    private static Value Multiply(string operation, Value left, Value right)
    {
        var first = left.AsNumber();
        var second = right.AsNumber();

        switch (operation)
        {
            case "/":
                if (second == 0)
                {
                    throw ExpressionException.DivideByZero();
                }

                return Value.FromNumber(first / second);
            case "%":
                if (second == 0)
                {
                    throw ExpressionException.DivideByZero();
                }

                return Value.FromNumber(first % second);
            default:
                return Value.FromNumber(first * second);
        }
    }

    private static Value Compare(string operation, Value left, Value right)
    {
        switch (operation)
        {
            case "==":
                return Value.FromBool(left.Equals(right));
            case "!=":
                return Value.FromBool(!left.Equals(right));
        }

        if (TryNumber(left, out var first) && TryNumber(right, out var second))
        {
            return Value.FromBool(operation switch
            {
                "<" => first < second,
                "<=" => first <= second,
                ">" => first > second,
                _ => first >= second,
            });
        }

        var order = string.Compare(left.AsText(), right.AsText(), StringComparison.OrdinalIgnoreCase);
        return Value.FromBool(operation switch
        {
            "<" => order < 0,
            "<=" => order <= 0,
            ">" => order > 0,
            _ => order >= 0,
        });
    }

    private static bool TryNumber(Value value, out double number)
    {
        if (value.Kind is ValueKind.Number)
        {
            number = value.Number;
            return true;
        }

        return double.TryParse(value.AsText(), NumberStyles.Any, CultureInfo.InvariantCulture,
            out number);
    }
}
