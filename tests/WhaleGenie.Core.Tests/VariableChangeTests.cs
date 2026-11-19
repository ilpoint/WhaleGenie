using System.Collections.Generic;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A watcher is told when a value moved. Two values that are not the same value are a move, even
/// when they read alike: a number written over the line that spelled it is a new value, and a
/// watcher that stayed quiet would be showing the wrong kind.
/// </summary>
public class VariableChangeTests
{
    [Fact]
    public void Writing_a_number_over_the_line_that_spelled_it_is_a_change()
    {
        var bag = new VariableBag();
        var changed = new List<string>();
        bag.Changed += changed.Add;

        bag.Set("count", Value.FromText("1"));
        bag.Set("count", Value.FromNumber(1));

        Assert.Equal(2, changed.Count);
    }

    [Fact]
    public void Writing_the_same_value_again_is_not_a_change()
    {
        var bag = new VariableBag();
        var changed = new List<string>();
        bag.Changed += changed.Add;

        bag.Set("count", Value.FromNumber(1));
        bag.Set("count", Value.FromNumber(1));

        Assert.Single(changed);
    }
}
