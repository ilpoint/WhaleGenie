using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using WhaleGenie.Core.Variables;


namespace WhaleGenie.Core.Expressions;

// Turning a tree into something a run can call.
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
            VariableNode variable => () => Resolve(variables, variable),
            ConcatNode run => Compiled(run, variables),
            ListNode list => Compiled(list, variables),
            CallNode call => Compiled(call, variables),
            UnaryNode unary => Compiled(unary, variables),
            BinaryNode binary => Compiled(binary, variables),
            FaultNode fault => () => throw ExpressionException.Syntax(fault.Message),
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

    /// <summary>
    /// A run of pieces as the one piece of text it reads as. Every part is worked out as its own
    /// value first, so a name that holds a number lands in the sentence as the number it holds
    /// rather than as the way the field happens to spell it.
    ///
    /// A name standing on its own in a run is read leniently: a name nothing defines reads as the
    /// name itself, so a misspelling shows up in the sentence the writer was composing instead of
    /// stopping a run that had nothing to do with it. A formula in the run is still worked out
    /// strictly, because a formula that cannot be worked out is a mistake rather than a word.
    /// </summary>
    private static Func<Value> Compiled(ConcatNode run, IVariableResolver variables)
    {
        var parts = run.Parts
            .Select(part => Compile(part, part is VariableNode ? new Lenient(variables) : variables))
            .ToList();
        return () => Value.FromText(string.Concat(parts.Select(part => part().AsText())));
    }

    /// <summary>A resolver that answers with the name itself when nothing defines it.</summary>
    private sealed class Lenient(IVariableResolver inner) : IVariableResolver
    {
        public bool TryGet(string name, out Value value)
        {
            if (inner.TryGet(name, out value))
            {
                return true;
            }

            value = Value.FromText(name);
            return true;
        }

        public bool TryGet(VariableKey key, out Value value)
        {
            if (inner.TryGet(key, out value))
            {
                return true;
            }

            value = Value.FromText(key.Name);
            return true;
        }
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

    private static Value Resolve(IVariableResolver variables, VariableNode variable)
        => variables.TryGet(VariableKey.From(variable), out var value)
            ? value
            : throw ExpressionException.UnknownVariable(variable.Name);

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

        // A date is compared as a moment, not as the line it is written as: two offsets of one
        // instant are one moment, and a date beside a date-shaped line is compared in time order.
        if ((left.Kind is ValueKind.Date || right.Kind is ValueKind.Date)
            && left.TryDate(out var moment)
            && right.TryDate(out var otherMoment))
        {
            var timing = moment.UtcTicks.CompareTo(otherMoment.UtcTicks);
            return Value.FromBool(operation switch
            {
                "<" => timing < 0,
                "<=" => timing <= 0,
                ">" => timing > 0,
                _ => timing >= 0,
            });
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
