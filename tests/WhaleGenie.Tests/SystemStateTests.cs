using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The one switch the page, the hotkey and the window write. It must tell its listeners only when
/// the value actually moved, because every listener reacts by arming a service or playing a sound.
/// </summary>
public class SystemStateTests
{
    [Fact]
    public void A_write_of_the_same_value_is_not_a_change()
    {
        var state = new SystemState(false);
        var changed = 0;
        state.Changed += () => changed++;

        state.Set(false);
        Assert.Equal(0, changed);

        state.Set(true);
        Assert.Equal(1, changed);
        Assert.True(state.IsEnabled);

        state.Set(true);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Toggling_moves_the_switch_both_ways()
    {
        var state = new SystemState(false);
        state.Toggle();
        Assert.True(state.IsEnabled);
        state.Toggle();
        Assert.False(state.IsEnabled);
    }
}
