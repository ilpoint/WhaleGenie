using SharpHook.Data;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What the program sent a moment ago, so that its own keys are not answered as though a person
/// had pressed them. The number pad is where this goes wrong: with NumLock off its keys arrive as
/// the navigation block, under names the macro never wrote.
/// </summary>
public class OwnInputTests
{
    /// <summary>
    /// A macro that presses NumPad7 with NumLock off has the key reported as Home. The gate has to
    /// know both names, or the shortcut bound to Home answers a key nobody pressed — which is how a
    /// run of keys ends up switching the whole program off halfway through.
    /// </summary>
    [Fact]
    public void A_number_pad_key_is_remembered_under_the_name_it_arrives_as()
    {
        WhaleGenieInputGate.Note(SharpKeys.Key(KeyCode.VcNumPad7));

        Assert.True(WhaleGenieInputGate.RecentlySent("NumPad7"));
        Assert.True(WhaleGenieInputGate.RecentlySent("Home"));
    }

    /// <summary>
    /// A key whose name nothing decides is remembered under that name alone: remembering more would
    /// take a person's key away from them, and only the number pad needs the second name.
    /// </summary>
    [Fact]
    public void A_key_that_keeps_its_name_is_remembered_under_that_name_only()
    {
        WhaleGenieInputGate.Note(SharpKeys.Key(KeyCode.VcA));

        Assert.True(WhaleGenieInputGate.RecentlySent("A"));
        Assert.False(WhaleGenieInputGate.RecentlySent("F5"));
        Assert.False(WhaleGenieInputGate.RecentlySent("Right"));
    }

    /// <summary>
    /// The whole length of a chord is remembered, so the modifiers of a chord the macro is sending
    /// are not answered either.
    /// </summary>
    [Fact]
    public void Every_key_of_a_chord_is_remembered()
    {
        WhaleGenieInputGate.Note(SharpKeys.Key(KeyCode.VcLeftControl));
        WhaleGenieInputGate.Note(SharpKeys.Key(KeyCode.VcNumPad1));

        Assert.True(WhaleGenieInputGate.RecentlySent("左Ctrl"));
        Assert.True(WhaleGenieInputGate.RecentlySent("End"));
    }
}
