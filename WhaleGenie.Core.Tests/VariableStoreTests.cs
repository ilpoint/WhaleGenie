using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

public class VariableStoreTests
{
    [Fact]
    public void A_local_value_hides_a_global_one_of_the_same_name()
    {
        var store = new VariableStore();
        store.Set("size", Value.FromNumber(1), VariableTarget.Global);
        store.Set("size", Value.FromNumber(2));

        Assert.True(store.TryGet("size", out var value));
        Assert.Equal(2, value.AsNumber());
    }

    [Fact]
    public void A_global_value_hides_a_system_one_of_the_same_name()
    {
        var store = new VariableStore();
        store.System.SetNumber("counter", 0);
        store.Set("counter", Value.FromNumber(7), VariableTarget.Global);

        Assert.True(store.TryGet("counter", out var value));
        Assert.Equal(7, value.AsNumber());
    }

    [Fact]
    public void Names_are_matched_without_caring_about_case()
    {
        var store = new VariableStore();
        store.Local.SetText("Name", "ada");

        Assert.True(store.TryGet("name", out var value));
        Assert.Equal("ada", value.AsText());
    }

    [Fact]
    public void An_unknown_name_is_reported_as_missing()
        => Assert.False(new VariableStore().TryGet("nope", out _));

    [Fact]
    public void Flatten_lists_every_scope_with_the_winning_value()
    {
        var store = new VariableStore();
        store.System.SetNumber("a", 1);
        store.System.SetNumber("b", 1);
        store.Global.SetNumber("b", 2);
        store.Local.SetNumber("b", 3);
        store.Local.SetNumber("c", 4);

        var flat = store.Flatten();
        Assert.Equal(1, flat["a"].AsNumber());
        Assert.Equal(3, flat["b"].AsNumber());
        Assert.Equal(4, flat["c"].AsNumber());
        Assert.Equal(3, flat.Count);
    }

    [Fact]
    public void The_store_is_a_resolver_an_expression_can_use()
    {
        var store = new VariableStore();
        store.SetNumber("count", 4);
        store.Set("names", Value.FromList(new[] { Value.FromText("a"), Value.FromText("b") }));

        Assert.Equal("4/2", Expression.Evaluate("$count + \"/\" + length($names)", store).AsText());
    }

    [Fact]
    public void Removing_a_value_takes_it_out_of_its_scope()
    {
        var bag = new VariableBag();
        bag.SetText("temp", "x");

        Assert.True(bag.Remove("temp"));
        Assert.False(bag.Contains("temp"));
        Assert.False(bag.Remove("temp"));
    }
}
