using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using Xunit;

namespace WhaleGenie.Core.Tests;

// Taking a name back out of a table, which is what a macro does when a value has served its turn.
public partial class MacroRunnerTests
{
    [Fact]
    public async Task Clearing_a_local_takes_the_name_out_of_the_table()
    {
        var store = Store(("done", 1));

        var (result, _) = await RunAsync(
            [Step("control.clearVariable", Param("name", "done"), Param("scope", "local"))],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.False(store.Local.Contains("done"));
    }

    [Fact]
    public async Task Clearing_a_global_puts_its_declared_value_back()
    {
        var store = Store();
        store.Global.SetNumber("total", 99);
        store.GlobalDefault = _ => Value.FromNumber(3);

        var (result, _) = await RunAsync(
            [Step("control.clearVariable", Param("name", "total"), Param("scope", "global"))],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.True(store.Global.TryGet("total", out var value));
        Assert.Equal(3, value.AsNumber());
    }

    [Fact]
    public async Task Clearing_a_global_that_was_never_declared_is_refused()
    {
        var store = Store();

        var (result, _) = await RunAsync(
            [Step("control.clearVariable", Param("name", "missing"), Param("scope", "global"))],
            variables: store);

        Assert.Equal(RunKeys.GlobalMustExist, result.Key);
    }
}
