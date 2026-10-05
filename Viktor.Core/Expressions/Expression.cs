using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Viktor.Core.Expressions;

/// <summary>
/// Reads and evaluates the small expression language the editor offers. The grammar stays
/// narrow on purpose: literals, <c>$variables</c>, the usual operators and a table of
/// functions. That is enough for the values a macro needs without turning into a script host.
/// </summary>
/// <remarks>
/// Examples: <c>$count + 1</c>, <c>upper($name)</c>, <c>split($text, ",")</c>,
/// <c>append($names, "Ada")</c>, <c>if($count &gt; 3, "many", "few")</c>.
/// </remarks>
public static class Expression
{
    /// <summary>Every built-in function, in the order the editor should list them.</summary>
    public static IReadOnlyList<ExpressionFunction> Functions => FunctionLibrary.Metadata;

    /// <summary>
    /// The variable names an expression reads, in the order they appear and without
    /// duplicates. Text that is not yet a valid expression still yields whatever names it
    /// holds, which is what the Variable Center needs while a macro is still being written.
    /// </summary>
    public static IReadOnlyList<string> ReferencedNames(string? source)
    {
        var names = new List<string>();
        if (string.IsNullOrEmpty(source))
        {
            return names;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < source.Length; index++)
        {
            if (source[index] != '$')
            {
                continue;
            }

            var start = index + 1;
            if (start >= source.Length || !(char.IsLetter(source[start]) || source[start] == '_'))
            {
                continue;
            }

            var end = start;
            while (end < source.Length && IsNameCharacter(source[end]))
            {
                end++;
            }

            var name = source[start..end];
            if (seen.Add(name))
            {
                names.Add(name);
            }

            index = end - 1;
        }

        return names;
    }

    /// <summary>
    /// Characters a variable name may hold. A hyphen is deliberately left out so that
    /// <c>$count-1</c> reads as a subtraction rather than as a variable named "count-1".
    /// </summary>
    private static bool IsNameCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '.';

    /// <summary>Evaluates an expression, throwing when it cannot be read.</summary>
    public static Value Evaluate(string? source, IVariableResolver? variables = null)
    {
        if (!TryEvaluate(source, variables, out var value, out var error))
        {
            throw error!;
        }

        return value;
    }

    /// <summary>
    /// Evaluates an expression and reports failure instead of throwing, which is what the
    /// editor uses to show a live error under the field.
    /// </summary>
    public static bool TryEvaluate(string? source, IVariableResolver? variables,
        out Value value, out ExpressionException? error)
    {
        try
        {
            value = EvaluateChecked(source, variables);
            error = null;
            return true;
        }
        catch (ExpressionException failure)
        {
            value = Value.Null;
            error = failure;
            return false;
        }
    }

    private static Value EvaluateChecked(string? source, IVariableResolver? variables)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Value.Null;
        }

        var tokens = Tokenizer.Read(source);
        return Parser.Parse(tokens, variables ?? EmptyVariableResolver.Instance)();
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

/// <summary>Splits expression text into the tokens the parser walks over.</summary>
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
/// Turns tokens into a tree of thunks. Building thunks rather than values keeps
/// <c>and</c>, <c>or</c> and <c>if</c> lazy, so the branch that is not taken never runs.
/// </summary>
file sealed class Parser
{
    private readonly List<Token> _tokens;
    private readonly IVariableResolver _variables;
    private int _index;

    private Parser(List<Token> tokens, IVariableResolver variables)
    {
        _tokens = tokens;
        _variables = variables;
    }

    public static Func<Value> Parse(List<Token> tokens, IVariableResolver variables)
    {
        var parser = new Parser(tokens, variables);
        var expression = parser.ReadOr();
        if (parser.Current.Kind != TokenKind.End)
        {
            throw ExpressionException.Syntax($"unexpected \"{parser.Current.Text}\"");
        }

        return expression;
    }

    private Token Current => _tokens[_index];

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

    private Func<Value> ReadOr()
    {
        var left = ReadAnd();
        while (TakeWord("or") || TakeSymbol("||"))
        {
            var captured = left;
            var right = ReadAnd();
            left = () => Value.FromBool(captured().AsBool() || right().AsBool());
        }

        return left;
    }

    private Func<Value> ReadAnd()
    {
        var left = ReadComparison();
        while (TakeWord("and") || TakeSymbol("&&"))
        {
            var captured = left;
            var right = ReadComparison();
            left = () => Value.FromBool(captured().AsBool() && right().AsBool());
        }

        return left;
    }

    private Func<Value> ReadComparison()
    {
        var left = ReadAdditive();
        while (Current.Kind == TokenKind.Symbol && Current.Text is "==" or "!=" or "<" or "<=" or ">" or ">=")
        {
            var operation = Take().Text;
            var captured = left;
            var right = ReadAdditive();
            left = () => Compare(operation, captured(), right());
        }

        return left;
    }

    private Func<Value> ReadAdditive()
    {
        var left = ReadMultiplicative();
        while (Current.Kind == TokenKind.Symbol && Current.Text is "+" or "-")
        {
            var operation = Take().Text;
            var captured = left;
            var right = ReadMultiplicative();
            left = operation == "+"
                ? () => Add(captured(), right())
                : () => Value.FromNumber(captured().AsNumber() - right().AsNumber());
        }

        return left;
    }

    private Func<Value> ReadMultiplicative()
    {
        var left = ReadUnary();
        while (Current.Kind == TokenKind.Symbol && Current.Text is "*" or "/" or "%")
        {
            var operation = Take().Text;
            var captured = left;
            var right = ReadUnary();
            left = () => Multiply(operation, captured(), right());
        }

        return left;
    }

    private Func<Value> ReadUnary()
    {
        if (TakeSymbol("-"))
        {
            var operand = ReadUnary();
            return () => Value.FromNumber(-operand().AsNumber());
        }

        if (TakeSymbol("!") || TakeWord("not"))
        {
            var operand = ReadUnary();
            return () => Value.FromBool(!operand().AsBool());
        }

        return ReadPrimary();
    }

    private Func<Value> ReadPrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                _index++;
                return () => Value.FromNumber(token.Number);

            case TokenKind.Text:
                _index++;
                return () => Value.FromText(token.Text);

            case TokenKind.Variable:
                _index++;
                return () => Resolve(token.Text);

            case TokenKind.Name:
                _index++;
                if (TakeSymbol("("))
                {
                    return Call(token.Text, ReadArguments());
                }

                return Constant(token.Text);

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
                var items = new List<Func<Value>>();
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

                return () => Value.FromList(items.Select(item => item()));

            default:
                throw ExpressionException.Syntax(token.Kind == TokenKind.End
                    ? "the expression ends too early"
                    : $"unexpected \"{token.Text}\"");
        }
    }

    private List<Func<Value>> ReadArguments()
    {
        var arguments = new List<Func<Value>>();
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

    private Value Resolve(string name)
        => _variables.TryGet(name, out var value) ? value : throw ExpressionException.UnknownVariable(name);

    private static Func<Value> Call(string name, List<Func<Value>> arguments)
    {
        if (!FunctionLibrary.TryGet(name, out var entry))
        {
            throw ExpressionException.UnknownFunction(name);
        }

        if (arguments.Count < entry.MinArguments || arguments.Count > entry.MaxArguments)
        {
            throw ExpressionException.ArgumentCount(entry.Signature);
        }

        return () => entry.Body(arguments);
    }

    private static Func<Value> Constant(string word) => word.ToLowerInvariant() switch
    {
        "true" => () => Value.FromBool(true),
        "false" => () => Value.FromBool(false),
        "null" => () => Value.Null,
        _ => throw ExpressionException.Syntax($"unknown word \"{word}\""),
    };

    private static Value Add(Value left, Value right)
    {
        if (left.Kind is ValueKind.Text or ValueKind.List || right.Kind is ValueKind.Text or ValueKind.List)
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

        return double.TryParse(value.AsText(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }
}
