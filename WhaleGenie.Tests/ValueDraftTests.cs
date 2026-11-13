using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The editing model keeps one value: the shape the field will store. These cases are the promises
/// that let the band, the caret and the confirm button all work from that one thing — a change is
/// seen in the drawing, a path names one place, and no line of text is ever read back into a value.
/// </summary>
public class ValueDraftTests
{
    [Fact]
    public void The_shape_a_draft_starts_with_is_the_value_it_hands_back()
    {
        var given = Formula.Binary("+", Formula.Name("count", "a7k2m9"), Formula.Number(1));

        var draft = new ValueDraft(given);

        Assert.Equal(given, draft.Value);
        Assert.Equal("a7k2m9", Assert.IsType<VariableNode>(
            Assert.IsType<BinaryNode>(draft.Value).Left).Id);
    }

    [Fact]
    public void The_band_is_drawn_from_the_shape_without_any_sign_in_front_of_a_name()
    {
        var draft = new ValueDraft(Formula.Call("upper", Formula.Name("who")));

        var call = Assert.IsType<SlotCall>(draft.Recipe);
        Assert.Equal("upper", call.Name);
        var name = Assert.IsType<SlotValue>(Assert.Single(call.Arguments).Items.Single());
        Assert.True(name.IsName);
        Assert.Equal("who", name.Text);
        Assert.DoesNotContain("$", draft.Recipe.Walk()
            .OfType<SlotValue>().Select(value => value.Text).Aggregate("", (a, b) => a + b));
    }

    [Fact]
    public void Every_piece_remembers_where_it_stands_in_the_tree()
    {
        var draft = new ValueDraft(Formula.Binary("+", Formula.Name("count"), Formula.Number(1)));

        var root = Assert.IsType<SlotSequence>(draft.Recipe);
        Assert.Equal(NodePath.Root, root.Path);
        Assert.Equal(NodePath.Root.Append(0), root.Items[0].Path);
        Assert.Equal(NodePath.Root.Append(1), root.Items[2].Path);
        Assert.Equal(new VariableNode("count"), root.Items[0].Source);
        Assert.Equal(new NumberNode("1", 1), root.Items[2].Source);
    }

    [Fact]
    public void Replacing_a_place_changes_both_the_value_and_the_band()
    {
        var draft = new ValueDraft(Formula.Binary("+", Formula.Name("count"), Formula.Number(1)));

        draft.Set(NodePath.Root.Append(0), Formula.Name("total"));

        Assert.Equal(new VariableNode("total"),
            Assert.IsType<BinaryNode>(draft.Value).Left);
        Assert.Equal("total", Assert.IsType<SlotValue>(
            Assert.IsType<SlotSequence>(draft.Recipe).Items[0]).Text);
    }

    [Fact]
    public void Putting_a_piece_keeps_what_was_already_there()
    {
        var draft = new ValueDraft(Formula.Text("hello "));
        draft.SetCaret(NodePath.Root);

        draft.Put(Formula.Name("who"));

        var run = Assert.IsType<ConcatNode>(draft.Value);
        Assert.Equal(2, run.Parts.Count);
        Assert.Equal(new TextNode("hello "), run.Parts[0]);
        Assert.Equal(new VariableNode("who"), run.Parts[1]);
    }

    [Fact]
    public void Taking_a_piece_away_leaves_a_hole_rather_than_a_broken_tree()
    {
        var draft = new ValueDraft(Formula.Binary("+", Formula.Name("count"), Formula.Number(1)));

        draft.Remove(NodePath.Root.Append(0));

        Assert.IsType<HoleNode>(Assert.IsType<BinaryNode>(draft.Value).Left);
        Assert.True(draft.HasHole());
        Assert.Contains(draft.Recipe.Walk(), piece => piece is SlotEmpty);
    }

    [Fact]
    public void Undo_and_redo_walk_the_shapes_not_lines_of_text()
    {
        var given = Formula.Name("count", "a7k2m9");
        var draft = new ValueDraft(given);
        draft.Set(NodePath.Root, Formula.Name("total"));

        Assert.True(draft.CanUndo);
        draft.Undo();
        Assert.Equal(given, draft.Value);
        Assert.Equal("a7k2m9", Assert.IsType<VariableNode>(draft.Value).Id);

        draft.Redo();
        Assert.Equal(new VariableNode("total"), draft.Value);
    }

    [Fact]
    public void Clearing_makes_an_empty_value_that_says_nothing()
    {
        var draft = new ValueDraft(Formula.Call("upper", Formula.Name("who")));

        draft.Clear();

        Assert.True(draft.IsEmpty);
        Assert.Equal(Formula.Nothing, draft.Value);
    }

    [Fact]
    public void A_call_keeps_the_places_it_still_asks_for()
    {
        var draft = new ValueDraft(Formula.Call("upper"));

        var call = Assert.IsType<SlotCall>(draft.Recipe);
        Assert.Empty(call.Arguments);
        Assert.NotNull(call.Ghost);
        Assert.True(call.Ghost!.Required);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_piece_has_a_place_in_a_stable_order(bool bare)
    {
        var value = Formula.Binary("+",
            Formula.Call("upper", Formula.Name("who", "a7k2m9")),
            Formula.Number(2));
        var (root, end) = SlotReader.Shape(value,
            Expression.Functions.ToDictionary(function => function.Name,
                StringComparer.OrdinalIgnoreCase), bare);

        Assert.True(end > 0);
        Assert.All(root.Walk(), piece =>
        {
            Assert.True(piece.Start >= 0, "A piece cannot start before the run.");
            Assert.True(piece.Start + piece.Length <= end, "A piece cannot pass the end of the run.");
            Assert.NotNull(piece.Path);
        });

        // Leaves are drawn in the order they stand, so a caret walking the band walks the value.
        var leaves = root.Walk().Where(piece => piece is SlotValue or SlotOperator).ToList();
        Assert.NotEmpty(leaves);
        for (var index = 1; index < leaves.Count; index++)
        {
            Assert.True(leaves[index].Start >= leaves[index - 1].Start,
                "A later piece cannot be drawn before an earlier one.");
        }

        // A sign belongs to the operator that holds it, so its walk names that operator. A value
        // names its own place in the tree.
        Assert.All(root.Walk().OfType<SlotValue>(),
            piece => Assert.False(piece.Path!.IsRoot,
                "A value names where it stands, not the value as a whole."));
    }

    [Fact]
    public void A_shape_carries_the_identity_of_its_names_into_the_band()
    {
        var draft = new ValueDraft(Formula.Name("count", "a7k2m9"));

        var piece = Assert.IsType<SlotValue>(draft.Recipe);
        Assert.Equal("count", piece.Text);
        Assert.DoesNotContain("$", piece.Text);
        Assert.Equal("a7k2m9", Assert.IsType<VariableNode>(piece.Source).Id);
        Assert.Equal(NodePath.Root, piece.Path);
    }
}
