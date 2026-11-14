using System;
using System.Collections.Generic;
using System.Threading;

namespace WhaleGenie.Core.Expressions;

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
    /// <summary>One row an expression left out, said as a log key and its arguments.</summary>
    internal sealed record Diagnostic(string Key, IReadOnlyList<object> Arguments);

    private static readonly AsyncLocal<Stack<List<Diagnostic>>?> Notes = new();

    /// <summary>Every built-in function, in the order the editor should list them.</summary>
    public static IReadOnlyList<ExpressionFunction> Functions => FunctionLibrary.Metadata;

    /// <summary>
    /// Starts a fresh place for a step to leave notes about rows it skipped. A step begins one
    /// before it runs and takes what it collected when it is over, so a note lands in the log of
    /// the step whose expression made it.
    /// </summary>
    internal static void BeginDiagnostics()
        => (Notes.Value ??= new Stack<List<Diagnostic>>()).Push([]);

    /// <summary>Takes the notes collected since the last beginning, and leaves the place empty.</summary>
    internal static IReadOnlyList<Diagnostic> TakeDiagnostics()
    {
        if (Notes.Value is not { Count: > 0 } stack)
        {
            return [];
        }

        var notes = stack.Pop();
        if (stack.Count == 0)
        {
            Notes.Value = null;
        }

        return notes;
    }

    /// <summary>Records one thing an expression skipped rather than silently dropping it.</summary>
    internal static void Report(string key, params object[] arguments)
    {
        if (Notes.Value is not { Count: > 0 } stack)
        {
            // Nothing is listening: an expression the editor evaluates on its own has no step
            // whose log could carry the note.
            return;
        }

        stack.Peek().Add(new Diagnostic(key, arguments));
    }

    /// <summary>
    /// The one place a line a person typed into a formula box becomes a shape. A name is said as
    /// the name itself and text is quoted; a half-written line is an empty place, which no cell
    /// can confirm until it is filled. Nothing else reads a line back into a value.
    /// </summary>
    public static ExpressionNode Read(string? source)
    {
        var written = (source ?? string.Empty).Trim();
        if (written.Length == 0)
        {
            return Formula.Nothing;
        }

        return TryParse(written, out var tree, out _) && tree is not null ? tree : Formula.Hole;
    }

    /// <summary>
    /// The variable names a tree reads, in the order they appear and without duplicates. A name
    /// inside a run of text counts the same as one standing on its own, because both are the same
    /// shape once the field has been read.
    /// </summary>
    public static IReadOnlyList<string> References(ExpressionNode? tree)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Collect(tree, name =>
        {
            if (seen.Add(name))
            {
                names.Add(name);
            }
        });

        return names;
    }

    private static void Collect(ExpressionNode? node, Action<string> found)
    {
        switch (node)
        {
            case VariableNode variable:
                found(variable.Name);
                break;
            case ConcatNode run:
                foreach (var part in run.Parts)
                {
                    Collect(part, found);
                }

                break;
            case ListNode list:
                foreach (var item in list.Items)
                {
                    Collect(item, found);
                }

                break;
            case CallNode call:
                foreach (var argument in call.Arguments)
                {
                    Collect(argument, found);
                }

                break;
            case UnaryNode unary:
                Collect(unary.Operand, found);
                break;
            case BinaryNode binary:
                Collect(binary.Left, found);
                Collect(binary.Right, found);
                break;
        }
    }

    /// <summary>
    /// The identities a tree is bound to, in the order they appear and without repeats. A reference
    /// written by hand has no identity and is not listed: it is still read by its name.
    /// </summary>
    public static IReadOnlyList<string> ReferenceIds(ExpressionNode? tree)
    {
        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        CollectIds(tree, id =>
        {
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        });

        return ids;
    }

    private static void CollectIds(ExpressionNode? node, Action<string> found)
    {
        switch (node)
        {
            case VariableNode { Id.Length: > 0 } variable:
                found(variable.Id);
                break;
            case ConcatNode run:
                foreach (var part in run.Parts)
                {
                    CollectIds(part, found);
                }

                break;
            case ListNode list:
                foreach (var item in list.Items)
                {
                    CollectIds(item, found);
                }

                break;
            case CallNode call:
                foreach (var argument in call.Arguments)
                {
                    CollectIds(argument, found);
                }

                break;
            case UnaryNode unary:
                CollectIds(unary.Operand, found);
                break;
            case BinaryNode binary:
                CollectIds(binary.Left, found);
                CollectIds(binary.Right, found);
                break;
        }
    }

    /// <summary>
    /// Renames every reference to one variable and nothing else. A piece of quoted text that
    /// happens to spell the name is left alone, because it is a shape rather than a spelling: the
    /// tree says which nodes are references, so nothing has to be guessed from the characters.
    ///
    /// <para>
    /// A field read off the name goes with it: renaming <c>where</c> also renames <c>where.age</c>,
    /// because that is the same value read one field further in. A name that merely begins with the
    /// same letters is a different name and stays.
    /// </para>
    /// </summary>
    public static ExpressionNode Rename(ExpressionNode tree, string oldName, string newName)
    {
        if (oldName.Length == 0 || string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return tree;
        }

        return tree switch
        {
            VariableNode variable when ReadOff(variable.Name, oldName)
                => variable with { Name = newName + variable.Name[oldName.Length..] },
            ConcatNode run => run with
            {
                Parts = [.. run.Parts.Select(part => Rename(part, oldName, newName))],
            },
            ListNode list => list with
            {
                Items = [.. list.Items.Select(item => Rename(item, oldName, newName))],
            },
            CallNode call => call with
            {
                Arguments = [.. call.Arguments.Select(argument => Rename(argument, oldName, newName))],
            },
            UnaryNode unary => unary with { Operand = Rename(unary.Operand, oldName, newName) },
            BinaryNode binary => binary with
            {
                Left = Rename(binary.Left, oldName, newName),
                Right = Rename(binary.Right, oldName, newName),
            },
            _ => tree,
        };
    }

    /// <summary>True when a name is the one, or a field read off it: <c>where</c> or <c>where.age</c>.</summary>
    private static bool ReadOff(string name, string of)
        => string.Equals(name, of, StringComparison.Ordinal)
            || (name.Length > of.Length
                && name[of.Length] == '.'
                && string.CompareOrdinal(name, 0, of, 0, of.Length) == 0);

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
    /// Works out a tree a field already holds. Nothing is read here: the shape a field keeps is
    /// the shape the engine runs, which is what leaves a run of text with a name in it and a
    /// formula with no way of being confused for one another.
    /// </summary>
    public static Value Evaluate(ExpressionNode tree, IVariableResolver? variables = null)
        => ExpressionCompiler.Compile(tree, variables ?? EmptyVariableResolver.Instance)();

    /// <summary>Works out a tree, reporting failure instead of throwing.</summary>
    public static bool TryEvaluate(ExpressionNode tree, IVariableResolver? variables,
        out Value value, out ExpressionException? error)
    {
        try
        {
            value = Evaluate(tree, variables);
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

    /// <summary>
    /// Evaluates an expression and reports failure instead of throwing, which is what the
    /// editor uses to show a live error under the field.
    /// </summary>
    public static bool TryEvaluate(string? source, IVariableResolver? variables,
        out Value value, out ExpressionException? error, bool bare = false)
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

    /// <summary>
    /// Reads a formula into the tree the editor shows. The tree is the language's own shape, so
    /// what the editor puts in front of the user and what the engine runs are the same thing.
    /// </summary>
    public static ExpressionNode Parse(string? source)
    {
        if (!TryParse(source, out var tree, out var error))
        {
            throw error!;
        }

        return tree!;
    }

    /// <summary>
    /// Reads a formula into a tree, reporting failure instead of throwing. A word of its own is a
    /// name, and a piece of text is quoted: the spelling has no sign in front of a name.
    /// </summary>
    public static bool TryParse(string? source, out ExpressionNode? tree,
        out ExpressionException? error)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            tree = null;
            error = ExpressionException.Syntax("the formula is empty");
            return false;
        }

        try
        {
            tree = ExpressionReader.Read(source);
            error = null;
            return true;
        }
        catch (ExpressionException failure)
        {
            tree = null;
            error = failure;
            return false;
        }
    }

    /// <summary>
    /// Writes a tree back out as a formula. A place nothing was put in is written as
    /// <paramref name="hole"/>, which is what the editor shows and never what the engine reads.
    /// </summary>
    public static string Render(ExpressionNode tree, string hole = "▢")
        => ExpressionWriter.Write(tree, hole);

    private static Value EvaluateChecked(string? source, IVariableResolver? variables)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return Value.Null;
        }

        var tree = ExpressionReader.Read(source);
        return ExpressionCompiler.Compile(tree, variables ?? EmptyVariableResolver.Instance)();
    }
}
