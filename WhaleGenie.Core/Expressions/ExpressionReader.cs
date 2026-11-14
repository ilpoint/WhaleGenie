using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Expressions;

/// <summary>Turns expression text into the tree the editor edits and the evaluator runs.</summary>
internal static class ExpressionReader
{
    /// <summary>
    /// Reads a formula into the tree it stands for. A word of its own is a name, a piece of text is
    /// quoted, and a number, a date or a call keeps its own piece. There is no second spelling for
    /// a name and no line that is read as a sentence instead of a formula.
    /// </summary>
    public static ExpressionNode Read(string source)
    {
        var parser = new Parser(source);
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
    Date,
    Text,
    Name,
    Symbol,
    End,
}

file readonly record struct Token(TokenKind Kind, string Text, double Number);

/// <summary>
/// Walks the tokens into a tree. Written the way the language reads: <c>or</c> opens the loosest
/// level and each step down binds more tightly, so a formula written without brackets still comes
/// out as the tree the writer would draw.
/// </summary>
file sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _index;

    public Parser(string source)
    {
        _tokens = ReadTokens(source);
    }

    /// <summary>
    /// The scanner's tokens in the shape the parser walks. The scanner is the one place the lexical
    /// rules live; this only names the kinds the parser switches on.
    /// </summary>
    private static List<Token> ReadTokens(string source)
    {
        var tokens = new List<Token>();
        foreach (var token in ExpressionTokens.Scan(source))
        {
            var kind = token.Kind switch
            {
                ExpressionTokenKind.Number => TokenKind.Number,
                ExpressionTokenKind.Date => TokenKind.Date,
                ExpressionTokenKind.Text => TokenKind.Text,
                ExpressionTokenKind.Name => TokenKind.Name,
                ExpressionTokenKind.Symbol => TokenKind.Symbol,
                ExpressionTokenKind.End => TokenKind.End,
                _ => throw new InvalidOperationException($"Unexpected token {token.Kind}."),
            };
            tokens.Add(new Token(kind, token.Text, token.Number));
        }

        return tokens;
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

            case TokenKind.Date:
                _index++;
                return new DateNode(token.Text);

            case TokenKind.Text:
                _index++;
                return new TextNode(token.Text);

            case TokenKind.Name:
                _index++;
                return TakeSymbol("(")
                    ? Call(token.Text, ReadArguments())
                    : Word(token.Text);

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

    /// <summary>
    /// A word that is not followed by a bracket. Three of them are the constants the language
    /// spells out; every other word is the name it is. A piece of text is quoted, so a name needs
    /// no sign and nothing about a word has to be guessed at.
    /// </summary>
    private ExpressionNode Word(string word) => word.ToLowerInvariant() switch
    {
        "true" => new ConstantNode("true"),
        "false" => new ConstantNode("false"),
        "null" => new ConstantNode("null"),
        _ => new VariableNode(word),
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
