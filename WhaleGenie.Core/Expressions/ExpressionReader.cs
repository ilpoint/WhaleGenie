using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using WhaleGenie.Core.Variables;


namespace WhaleGenie.Core.Expressions;

// Reading written text into that tree, and the tokens it is cut into.
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
                if (index >= source.Length || !VariableName.IsStart(source[index]))
                {
                    throw ExpressionException.Syntax("a variable name is missing after \"$\"");
                }

                while (index < source.Length && VariableName.IsPart(source[index]))
                {
                    index++;
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
