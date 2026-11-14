using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Every kind of node the expression tree can hold, with one sample of each. The list is the
/// contract: a node type added to the language without a sample fails the first test, and a branch
/// the walker forgets fails the second. Reading the names a tree holds and renaming one go through
/// the same recursion, so one sample proves both.
/// </summary>
public class ExpressionCoverageTests
{
    private static readonly IReadOnlyDictionary<Type, ExpressionNode> Samples =
        new Dictionary<Type, ExpressionNode>
        {
            [typeof(TextNode)] = Formula.Text("共 "),
            [typeof(NumberNode)] = Formula.Number(1.5),
            [typeof(DateNode)] = Formula.Date("2026-11-15"),
            [typeof(ConstantNode)] = Formula.Constant("true"),
            [typeof(HoleNode)] = Formula.Hole,
            [typeof(VariableNode)] = Formula.Name("probe"),
            [typeof(ConcatNode)] = Formula.Run([Formula.Text("共 "), Formula.Name("probe")]),
            [typeof(ListNode)] = Formula.List(Formula.Name("probe"), Formula.Number(2)),
            [typeof(CallNode)] = Formula.Call("upper", Formula.Name("probe")),
            [typeof(UnaryNode)] = Formula.Unary("not", Formula.Name("probe")),
            [typeof(BinaryNode)] = Formula.Binary("+", Formula.Name("probe"), Formula.Number(1)),
        };

    /// <summary>Nodes that hold no name, so a rename has nothing to reach inside them.</summary>
    private static readonly HashSet<Type> Nameless =
    [
        typeof(TextNode),
        typeof(NumberNode),
        typeof(DateNode),
        typeof(ConstantNode),
        typeof(HoleNode),
    ];

    [Fact]
    public void Every_node_kind_has_a_sample()
    {
        var kinds = typeof(ExpressionNode).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ExpressionNode)) && !type.IsAbstract)
            .ToList();

        var missing = kinds.Where(kind => !Samples.ContainsKey(kind))
            .Select(kind => kind.Name)
            .ToList();

        Assert.True(missing.Count == 0,
            "no sample for: " + string.Join(", ", missing));
    }

    /// <summary>
    /// A name inside any node that can hold one is found, and renaming reaches it. A node the walker
    /// does not descend into fails here rather than quietly losing a reference in a saved macro.
    /// </summary>
    [Fact]
    public void A_name_in_every_node_that_holds_one_is_read_and_renamed()
    {
        foreach (var (kind, sample) in Samples.Where(pair => !Nameless.Contains(pair.Key)))
        {
            Assert.True(Expression.References(sample).Contains("probe"), kind.Name);

            var renamed = Expression.Rename(sample, "probe", "renamed");

            Assert.True(Expression.References(renamed).Contains("renamed"), kind.Name);
            Assert.DoesNotContain("probe", Expression.References(renamed));
        }
    }

    /// <summary>Text that spells a name is text, not a reference.</summary>
    [Fact]
    public void A_node_without_a_name_reads_no_names()
    {
        foreach (var (kind, sample) in Samples.Where(pair => Nameless.Contains(pair.Key)))
        {
            Assert.Empty(Expression.References(sample));
            Assert.Equal(sample, Expression.Rename(sample, "probe", "renamed"));
        }
    }

    /// <summary>
    /// Every node can be drawn as the one line a person reads, so a node type the writer does not
    /// know shows up here rather than as an empty box in the editor.
    /// </summary>
    [Fact]
    public void Every_node_kind_is_drawn_as_a_line()
    {
        foreach (var (kind, sample) in Samples)
        {
            Assert.NotEmpty(Expression.Render(sample));
            Assert.NotEmpty(Expression.Render(sample, hole: "_"));
        }
    }

    /// <summary>
    /// Every shape the editor can hold is the shape the file writes and reads back, except the place
    /// nobody has filled in yet, which is not a value and is never written.
    /// </summary>
    [Fact]
    public void Every_node_kind_except_a_hole_survives_the_file()
    {
        foreach (var (kind, sample) in Samples.Where(pair => pair.Key != typeof(HoleNode)))
        {
            var written = FormulaJson.Write(sample);
            Assert.Equal(written?.ToJsonString(), FormulaJson.Write(FormulaJson.Read(written))?.ToJsonString());
        }
    }
}
