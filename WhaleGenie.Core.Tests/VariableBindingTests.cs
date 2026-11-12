using System.Collections.Generic;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A name is a binding and a value is kept by identity, so the two move apart cleanly: a name can be
/// pointed somewhere else without copying a value, and a value nobody names is gone.
/// </summary>
public class VariableBindingTests
{
    [Fact]
    public void A_name_nothing_declared_is_given_an_identity_of_its_own()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(3));

        Assert.False(string.IsNullOrEmpty(bag.IdOf("total")));
        Assert.Equal(1, bag.Count);
        Assert.Equal(3d, bag.Values["total"].AsNumber());
    }

    [Fact]
    public void Writing_one_name_again_keeps_one_slot()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(1));
        bag.Set("total", Value.FromNumber(2));

        Assert.Equal(1, bag.Count);
        Assert.Equal(2d, bag.Values["total"].AsNumber());
    }

    [Fact]
    public void Removing_a_name_takes_its_value_with_it()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(1));

        Assert.True(bag.Remove("total"));
        Assert.False(bag.Contains("total"));
        Assert.Equal(0, bag.Count);
        Assert.Empty(bag.Values);
    }

    /// <summary>The snapshot is a copy: a watcher must not be able to change the table by writing to it.</summary>
    [Fact]
    public void The_values_a_watcher_reads_are_a_copy()
    {
        var bag = new VariableBag();
        bag.Set("total", Value.FromNumber(1));

        var seen = bag.Values;
        if (seen is IDictionary<string, Value> writable)
        {
            writable["total"] = Value.FromNumber(9);
        }

        Assert.Equal(1d, bag.Values["total"].AsNumber());
    }

    /// <summary>
    /// Two step outputs naming one result is how a macro overwrites a value: the second one takes
    /// the name, and the value nobody can name any more goes with it.
    /// </summary>
    [Fact]
    public void A_second_step_output_takes_the_name_and_the_first_value_goes()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("rows", "stepA1"), Value.FromNumber(1));
        bag.Set(new VariableKey("rows", "stepB2"), Value.FromNumber(2));

        Assert.Equal("stepB2", bag.IdOf("rows"));
        Assert.False(bag.ContainsId("stepA1"));
        Assert.Equal(1, bag.Count);
    }
}
