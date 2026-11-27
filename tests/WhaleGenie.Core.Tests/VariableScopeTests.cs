using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The layers a name is read through. Local beats global, global beats the machine's own, and a
/// called macro reads its own table rather than the caller's — the order is the whole rule, so it
/// is stated once here.
/// </summary>
public class VariableScopeTests
{
    [Fact]
    public void A_local_value_shadows_a_global_one_which_shadows_a_system_one()
    {
        var store = new VariableStore();
        store.System.Set("x", Value.FromNumber(1));
        store.Global.Set("x", Value.FromNumber(2));

        Assert.True(store.TryGet("x", out var global));
        Assert.Equal(2, global.AsNumber());

        store.Local.Set("x", Value.FromNumber(3));
        Assert.True(store.TryGet("x", out var local));
        Assert.Equal(3, local.AsNumber());

        store.Local.Remove("x");
        Assert.True(store.TryGet("x", out var backToGlobal));
        Assert.Equal(2, backToGlobal.AsNumber());

        store.Global.Remove("x");
        Assert.True(store.TryGet("x", out var system));
        Assert.Equal(1, system.AsNumber());
    }

    [Fact]
    public void A_called_macro_reads_its_own_table_and_the_caller_gets_its_own_back()
    {
        var store = new VariableStore();
        store.Local.Set("x", Value.FromText("caller"));

        var called = new VariableBag();
        called.Set("x", Value.FromText("callee"));
        store.PushLocal(called);

        Assert.True(store.TryGet("x", out var inside));
        Assert.Equal("callee", inside.AsText());

        Assert.Same(called, store.PopLocal());
        Assert.True(store.TryGet("x", out var after));
        Assert.Equal("caller", after.AsText());
    }

    [Fact]
    public void A_global_written_by_one_run_is_read_by_the_next_one()
    {
        var shared = new VariableBag();
        var first = new VariableStore(shared);
        var second = new VariableStore(shared);

        first.Global.Set("total", Value.FromNumber(7));

        Assert.True(second.TryGet("total", out var seen));
        Assert.Equal(7, seen.AsNumber());
    }
}
