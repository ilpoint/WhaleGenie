using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Editing;
using Xunit;

namespace WhaleGenie.Tests;

public class EditorKeyPadTests
{
    [Fact]
    public void Every_key_name_has_a_cap()
    {
        var caps = EditorKeyPad.Build("key").Rows.SelectMany(row => row).ToList();
        var covered = caps.Select(cap => KeyNames.ResolveCode(cap.Value))
            .OfType<HookCode>()
            .ToHashSet();

        Assert.All(KeyNames.Names, name =>
        {
            var code = KeyNames.ResolveCode(name);
            Assert.NotNull(code);
            Assert.Contains(code!.Value, covered);
        });
    }

    [Fact]
    public void A_key_run_uses_the_same_caps()
    {
        var key = EditorKeyPad.Build("key");
        var run = EditorKeyPad.Build("keys");
        Assert.Equal(key.Rows.Count, run.Rows.Count);
        Assert.Equal("keys", run.Mode);
        Assert.Contains(run.Rows.SelectMany(row => row), cap => cap.Value == "F13");
        Assert.Contains(run.Rows.SelectMany(row => row), cap => cap.Value == "NumPad7");
    }

    [Fact]
    public void The_character_pad_stays_within_the_hotstring_limit()
    {
        var pad = EditorCharacterPad.Build();
        Assert.Equal(40, pad.MaximumLength);
        Assert.Contains(pad.Rows.SelectMany(row => row), cap => cap.Kind == "shift");
        Assert.Contains(pad.Rows.SelectMany(row => row), cap => cap.Normal == "a" && cap.Shifted == "A");
    }
}
