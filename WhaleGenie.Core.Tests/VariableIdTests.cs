using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The identity a reference carries is the first thing a lookup uses. The name stays the storage
/// key so a watcher, a script and a saved file keep speaking the same language, but a bound
/// reference must not be hijacked by another value that happens to share its spelling.
/// </summary>
public class VariableBagIdTests
{
    [Fact]
    public void An_id_beats_the_name_beside_it()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("count", "a7k2m9"), Value.FromNumber(3));
        bag.Set(new VariableKey("total", "a7k2m9"), Value.FromNumber(7));

        Assert.True(bag.TryGet(new VariableKey("total", "a7k2m9"), out var value));
        Assert.Equal(7d, value.AsNumber());
        Assert.Equal("count", bag.NameOf("a7k2m9"));
        Assert.Single(bag.Values);
    }

    [Fact]
    public void An_id_that_is_gone_falls_back_to_the_name()
    {
        var bag = new VariableBag();
        bag.Set("count", Value.FromNumber(4));

        Assert.True(bag.TryGet(new VariableKey("count", "gone99"), out var value));
        Assert.Equal(4d, value.AsNumber());
    }

    [Fact]
    public void A_dynamic_write_takes_the_declared_entry()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("count", "a7k2m9"), Value.FromNumber(1));

        bag.Set("count", Value.FromNumber(2));

        Assert.Equal("a7k2m9", bag.IdOf("count"));
        Assert.True(bag.TryGetById("a7k2m9", out var value));
        Assert.Equal(2d, value.AsNumber());
        Assert.Single(bag.Values);
    }

    [Fact]
    public void Adding_and_changing_by_id_are_one_step()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("count", "a7k2m9"), Value.FromNumber(2));

        Assert.True(bag.TryAddNumber(new VariableKey("count", "a7k2m9"), 3, out var sum));
        Assert.Equal(5d, sum);

        Assert.True(bag.TryUpdate(new VariableKey("count", "a7k2m9"),
            _ => Value.FromNumber(9), out var updated));
        Assert.Equal(9d, updated.AsNumber());
    }

    [Fact]
    public void Renaming_keeps_the_id()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("count", "a7k2m9"), Value.FromNumber(1));

        Assert.True(bag.Rename("count", "total"));

        Assert.False(bag.TryGet("count", out _));
        Assert.True(bag.TryGetById("a7k2m9", out var value));
        Assert.Equal(1d, value.AsNumber());
        Assert.Equal("a7k2m9", bag.IdOf("total"));
    }

    [Fact]
    public void Values_are_still_keyed_by_name()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("count", "a7k2m9"), Value.FromNumber(1));

        Assert.True(bag.Values.ContainsKey("count"));
        Assert.False(bag.Values.ContainsKey("a7k2m9"));
    }

    [Fact]
    public void Another_declaration_cannot_take_a_name_this_one_holds()
    {
        var bag = new VariableBag();
        bag.Declare("a7k2m9");
        bag.Set(new VariableKey("total", "a7k2m9"), Value.FromNumber(1));

        var refusal = Assert.Throws<VariableConflictException>(() =>
            bag.Set(new VariableKey("total", "b8m3n1"), Value.FromNumber(2)));

        Assert.Equal("total", refusal.Name);
        Assert.Equal("a7k2m9", refusal.DeclaredId);
        Assert.True(bag.TryGet(new VariableKey("total", "a7k2m9"), out var value));
        Assert.Equal(1d, value.AsNumber());
    }

    /// <summary>
    /// Two steps naming their own result the same thing is how a macro overwrites a value, and it
    /// must keep working: neither identity is a declaration, so neither can own the name.
    /// </summary>
    [Fact]
    public void Two_step_outputs_with_one_name_still_overwrite()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("rows", "stepA1"), Value.FromNumber(1));
        bag.Set(new VariableKey("rows", "stepB2"), Value.FromNumber(2));

        Assert.Equal(2d, bag.Values["rows"].AsNumber());
        Assert.Equal("stepB2", bag.IdOf("rows"));
        Assert.False(bag.ContainsId("stepA1"));
    }

    [Fact]
    public void A_declaration_is_forgotten_when_the_run_starts_over()
    {
        var bag = new VariableBag();
        bag.Declare("a7k2m9");
        bag.Set(new VariableKey("total", "a7k2m9"), Value.FromNumber(1));

        bag.ResetDeclarations();
        bag.Set(new VariableKey("total", "b8m3n1"), Value.FromNumber(2));

        Assert.Equal(2d, bag.Values["total"].AsNumber());
    }

    /// <summary>
    /// A renamed declaration keeps its identity under a new name. Seeding a result placeholder
    /// asks about a name the identity has already left, and must not grow a second entry for it.
    /// </summary>
    [Fact]
    public void An_identity_written_under_another_name_is_not_added_twice()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("count", "a7k2m9"), Value.FromNumber(1));

        Assert.False(bag.SetIfAbsent(new VariableKey("total", "a7k2m9"), Value.FromNumber(2)));
        Assert.False(bag.Contains("total"));
        Assert.Single(bag.Values);
        Assert.True(bag.TryGet(new VariableKey("total", "a7k2m9"), out var value));
        Assert.Equal(1d, value.AsNumber());
    }

    [Fact]
    public void Restoring_a_name_puts_its_identity_back()
    {
        var bag = new VariableBag();
        bag.Set(new VariableKey("count", "a7k2m9"), Value.FromNumber(1));
        bag.Set(new VariableKey("count", "loop77"), Value.FromNumber(9));

        bag.Restore("count", "a7k2m9", Value.FromNumber(1));

        Assert.Equal("a7k2m9", bag.IdOf("count"));
        Assert.False(bag.ContainsId("loop77"));
        Assert.Equal(1d, bag.Values["count"].AsNumber());
    }
}

/// <summary>The three scopes a run reads from, with the identity looked at before the spelling.</summary>
public class VariableStoreIdTests
{
    [Fact]
    public void Without_an_id_the_order_is_what_it_was()
    {
        var store = new VariableStore();
        store.Global.Set("count", Value.FromNumber(1));
        store.Local.Set("count", Value.FromNumber(2));

        Assert.True(store.TryGet("count", out var value));
        Assert.Equal(2d, value.AsNumber());
    }

    [Fact]
    public void A_global_id_is_not_shadowed_by_a_local_name()
    {
        var store = new VariableStore();
        store.Global.Set(new VariableKey("count", "glob42"), Value.FromNumber(1));
        store.Local.Set(new VariableKey("count", "loc123"), Value.FromNumber(2));

        Assert.True(store.TryGet(new VariableKey("count", "glob42"), out var global));
        Assert.Equal(1d, global.AsNumber());

        Assert.True(store.TryGet(new VariableKey("count", "loc123"), out var local));
        Assert.Equal(2d, local.AsNumber());
    }

    [Fact]
    public void A_system_name_is_its_own_identity()
    {
        var store = new VariableStore();
        store.System.Set("sys.clipboard", Value.FromText("hello"));

        Assert.True(store.TryGet(new VariableKey("sys.clipboard"), out var value));
        Assert.Equal("hello", value.AsText());
    }
}
