using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using Xunit;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// A run's own table has a ceiling. A name built while the macro runs is kept until the run ends,
/// so a loop that writes a new one every round is the shape that reaches it; saying so is better
/// than growing until the machine gives up.
/// </summary>
public class VariableTableFullTests
{
    [Fact]
    public void A_new_name_past_the_ceiling_is_refused()
    {
        var bag = new VariableBag { Capacity = 2 };
        bag.Set("one", Value.FromNumber(1));
        bag.Set("two", Value.FromNumber(2));

        var refusal = Assert.Throws<VariableTableFullException>(() =>
            bag.Set("three", Value.FromNumber(3)));

        Assert.Equal(2, refusal.Capacity);
        Assert.Equal(2, bag.Count);
    }

    [Fact]
    public void Writing_a_name_the_table_already_holds_is_still_allowed()
    {
        var bag = new VariableBag { Capacity = 1 };
        bag.Set("one", Value.FromNumber(1));

        bag.Set("one", Value.FromNumber(2));

        Assert.Equal(2d, bag.Values["one"].AsNumber());
    }
}
