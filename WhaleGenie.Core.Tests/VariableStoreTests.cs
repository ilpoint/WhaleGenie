using System;
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

    /// <summary>
    /// A value that is only true for the moment it is read — the clock, the pointer, the clipboard —
    /// is asked for every time rather than kept, which is what makes reading it twice mean two
    /// answers. A macro counting seconds or watching the pointer depends on that.
    /// </summary>
    [Fact]
    public void A_live_value_is_read_every_time_it_is_asked_for()
    {
        var store = new VariableStore { Live = new Ticking(10) };

        Assert.True(store.TryGet("sys.tick", out var first));
        Assert.Equal(10, first.AsNumber());

        Assert.True(store.TryGet("sys.tick", out var second));
        Assert.Equal(11, second.AsNumber());

        // And through an expression, which is how the fields of a step read it.
        Assert.Equal("12", Expression.Evaluate("$sys.tick", store).AsText());
    }

    /// <summary>
    /// What a macro has worked out beats what the machine answers, so the round of a loop is the
    /// round rather than whatever the same name means outside one.
    /// </summary>
    [Fact]
    public void A_local_value_hides_a_live_one_of_the_same_name()
    {
        var store = new VariableStore { Live = new Ticking(10) };
        store.Local.SetNumber("sys.tick", 99);

        Assert.True(store.TryGet("sys.tick", out var value));
        Assert.Equal(99, value.AsNumber());
    }

    /// <summary>
    /// A name the live source does not know keeps travelling, so a macro that misspelled one is told
    /// about it rather than reading nothing.
    /// </summary>
    [Fact]
    public void A_name_the_live_source_does_not_know_is_still_missing()
        => Assert.False(new VariableStore { Live = new Ticking(10) }.TryGet("sys.nope", out _));

    /// <summary>
    /// The watch list says what a macro reads while it runs, so the values that move are read again
    /// for it instead of being reported at whatever they held when the run began.
    /// </summary>
    [Fact]
    public void Flatten_reads_the_live_values_again()
    {
        var store = new VariableStore { Live = new Ticking(1) };
        store.System.SetNumber("sys.tick", 0);

        Assert.Equal(1, store.Flatten()["sys.tick"].AsNumber());
        Assert.Equal(2, store.Flatten()["sys.tick"].AsNumber());
    }

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

    /// <summary>
    /// A stand-in for the machine: it knows one name, and that name counts up every time it is read,
    /// which is what a clock or a moving pointer looks like from here.
    /// </summary>
    private sealed class Ticking(int first) : ILiveVariables
    {
        private int _reads;

        public bool TryGet(string name, out Value value)
        {
            if (!string.Equals(name, "sys.tick", StringComparison.OrdinalIgnoreCase))
            {
                value = Value.Null;
                return false;
            }

            value = Value.FromNumber(first + _reads++);
            return true;
        }
    }
}
