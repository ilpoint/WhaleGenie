using System;
using System.Collections.Generic;
using System.Threading;

namespace WhaleGenie.Core.Expressions;

/// <summary>
/// Works out the values a field holds. A field keeps a shape, built a piece at a time in the
/// editor; there is no line of text that is read back into one, so nothing here has to guess what
/// a word means from the characters around it. <see cref="Render"/> draws a shape as a line for
/// the places that show one, and is the only direction text travels — out, never in.
/// </summary>
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
    /// The variable names a tree reads, in the order they appear and without duplicates. A name
    /// inside a run of text counts the same as one standing on its own, because both are the same
    /// shape once the field has been read.
    /// </summary>
    public static IReadOnlyList<string> References(ExpressionNode? tree)
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (tree is not null)
        {
            Map(tree, variable =>
            {
                if (seen.Add(variable.Name))
                {
                    names.Add(variable.Name);
                }

                return variable;
            });
        }

        return names;
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

        return Map(tree, variable => ReadOff(variable.Name, oldName)
            ? variable with { Name = newName + variable.Name[oldName.Length..] }
            : variable);
    }

    /// <summary>
    /// One node with the same shape and every name in it put through <paramref name="change"/>.
    /// This is the only place the tree's branches are written down: reading the names a tree holds
    /// and renaming one both come through here, so a node type added to the language is handled by
    /// both at once. <c>ExpressionCoverageTests</c> fails when a node type has no sample, which is
    /// what keeps a new branch from being forgotten.
    /// </summary>
    private static ExpressionNode Map(ExpressionNode tree, Func<VariableNode, VariableNode> change)
        => tree switch
        {
            VariableNode variable => change(variable),
            ConcatNode run => run with
            {
                Parts = [.. run.Parts.Select(part => Map(part, change))],
            },
            ListNode list => list with
            {
                Items = [.. list.Items.Select(item => Map(item, change))],
            },
            CallNode call => call with
            {
                Arguments = [.. call.Arguments.Select(argument => Map(argument, change))],
            },
            GroupNode group => group with { Inner = Map(group.Inner, change) },
            UnaryNode unary => unary with { Operand = Map(unary.Operand, change) },
            BinaryNode binary => binary with
            {
                Left = Map(binary.Left, change),
                Right = Map(binary.Right, change),
            },
            _ => tree,
        };

    /// <summary>True when a name is the one, or a field read off it: <c>where</c> or <c>where.age</c>.</summary>
    private static bool ReadOff(string name, string of)
        => string.Equals(name, of, StringComparison.Ordinal)
            || (name.Length > of.Length
                && name[of.Length] == '.'
                && string.CompareOrdinal(name, 0, of, 0, of.Length) == 0);

    /// <summary>
    /// Works out a tree a field already holds. Nothing is read here: the shape a field keeps is
    /// the shape the engine runs, which is what leaves a run of text with a name in it and a
    /// formula with no way of being confused for one another.
    /// </summary>
    // The two-argument form keeps the optional shape the shipped baseline records. The
    // wider overload takes the clock and dice, so it is the one with more parameters — RS0027
    // asks for exactly that and cannot see it through the overload pair written here.
#pragma warning disable RS0027
    public static Value Evaluate(ExpressionNode tree, IVariableResolver? variables = null)
        => Evaluate(tree, variables, EvaluationContext.System);
#pragma warning restore RS0027

    /// <summary>
    /// The same, against the clock and dice the caller hands in. A check that reads "now" twice has
    /// to get one answer, and an editor preview of a date formula has to be the day the person is
    /// looking at rather than whichever moment the evaluation happened to run.
    /// </summary>
    public static Value Evaluate(ExpressionNode tree, IVariableResolver? variables,
        EvaluationContext context)
        => ExpressionCompiler.Compile(tree, variables ?? EmptyVariableResolver.Instance,
            context ?? EvaluationContext.System)();

    /// <summary>Works out a tree, reporting failure instead of throwing.</summary>
    public static bool TryEvaluate(ExpressionNode tree, IVariableResolver? variables,
        out Value value, out ExpressionException? error)
        => TryEvaluate(tree, variables, EvaluationContext.System, out value, out error);

    /// <summary>The same, against the clock and dice the caller hands in.</summary>
    public static bool TryEvaluate(ExpressionNode tree, IVariableResolver? variables,
        EvaluationContext context, out Value value, out ExpressionException? error)
    {
        try
        {
            value = Evaluate(tree, variables, context);
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
    /// Writes a tree out as a line to look at. A place nothing was put in is written as
    /// <paramref name="hole"/>. This is the only text this class produces: it is drawn from a
    /// shape for the interface, and no line is ever read back into one.
    /// </summary>
    public static string Render(ExpressionNode tree, string hole = "▢")
        => ExpressionWriter.Write(tree, hole);
}
