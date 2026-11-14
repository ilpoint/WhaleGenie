using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The round number of a loop is a value only a run has. It is in the catalogue so the pickers offer
/// it like any other built-in, and it has no seed so reading it outside a loop is reading a name no
/// run wrote rather than a zero that looks like a round.
/// </summary>
public class SystemVariableTests
{
    [Fact]
    public void The_loop_round_is_offered_as_a_system_variable()
    {
        var definition = Assert.Single(VariableCatalog.SystemVariables,
            variable => variable.Name == VariableStore.LoopIndexName);

        Assert.Equal(ValueKind.Number, definition.Type);
        Assert.False(definition.Seeded);
    }

    [Fact]
    public void The_loop_round_has_no_value_before_a_loop()
    {
        var store = MacroVariables.Seed();

        Assert.False(store.System.Contains(VariableStore.LoopIndexName));
        Assert.False(store.TryGet(VariableStore.LoopIndexName, out _));
    }
}
