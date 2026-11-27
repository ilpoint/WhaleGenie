using System.Linq;

using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Renaming the name a field reads, and listing the names a shape holds. A name in quoted text is a
/// shape rather than a spelling, so renaming never reaches inside it, while a field read off the
/// name goes with it.
/// </summary>
public class ExpressionRenameTests
{
    [Fact]
    public void Renaming_nothing_or_a_name_to_itself_changes_nothing()
    {
        var tree = Formula.Binary("+", Formula.Name("where"), Formula.Number(1));

        Assert.Same(tree, Expression.Rename(tree, string.Empty, "w"));
        Assert.Same(tree, Expression.Rename(tree, "where", "where"));
    }

    [Fact]
    public void Renaming_reaches_every_piece_of_a_shape()
    {
        var tree = Formula.Binary("+",
            Formula.Run([Formula.Name("where"), Formula.Text("where")]),
            Formula.Call("max", Formula.Name("where"), Formula.List(Formula.Name("where"))));

        var renamed = Expression.Rename(tree, "where", "w");

        Assert.Equal(["w"], Expression.References(renamed));
        Assert.Equal("w + \"where\" + max(w, [w])", Expression.Render(renamed));
    }

    [Fact]
    public void A_field_read_off_the_name_goes_with_it_and_a_longer_name_does_not()
    {
        var tree = Formula.Binary("==",
            Formula.Name("where.age"),
            Formula.Binary("==", Formula.Name("wherefore"), Formula.Name("where")));

        var renamed = Expression.Rename(tree, "where", "w");

        Assert.Equal(["w.age", "wherefore", "w"], Expression.References(renamed));
    }

    [Fact]
    public void The_names_a_shape_holds_are_listed_once_in_the_order_they_appear()
    {
        var tree = Formula.Binary("+",
            Formula.Run([Formula.Name("b"), Formula.Name("A")]),
            Formula.Binary("==", Formula.Name("a"), Formula.Name("B")));

        Assert.Equal(["b", "A"], Expression.References(tree));
        Assert.Equal(["A"], Expression.References(Formula.Run([Formula.Name("A"), Formula.Name("a")])));
        Assert.Empty(Expression.References(null));
        Assert.Empty(Expression.References(Formula.Text("a")));
    }

    [Fact]
    public void A_group_a_prefix_and_a_record_hold_their_names()
    {
        var tree = new GroupNode(Formula.Unary("not", Formula.Name("ready")));

        Assert.Equal(["ready"], Expression.References(tree));
        Assert.Single(Expression.References(Expression.Rename(tree, "ready", "open")));
        Assert.Equal(["open"], Expression.References(Expression.Rename(tree, "ready", "open")));
    }
}
