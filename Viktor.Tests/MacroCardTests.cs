using Viktor.Models;

namespace Viktor.Tests;

/// <summary>What a macro card shows: the icon its trigger picks and the binding it previews.</summary>
public class MacroCardTests
{
    [Fact]
    public void A_key_binding_is_previewed_with_the_keyboard_icon()
    {
        var macro = new MacroItem { BindKey = "右Shift" };

        Assert.True(macro.IsKeyboardTrigger);
        Assert.False(macro.IsMouseTrigger);
        Assert.Equal("右Shift", macro.TriggerPreview);
    }

    [Theory]
    [InlineData("Mouse Left")]
    [InlineData("Mouse Wheel Up")]
    [InlineData("Wheel Left")]
    [InlineData("Mouse XButton1")]
    public void A_mouse_binding_is_previewed_with_the_mouse_icon(string binding)
    {
        var macro = new MacroItem { BindKey = binding };

        Assert.True(macro.IsMouseTrigger);
        Assert.False(macro.IsKeyboardTrigger);
    }

    [Fact]
    public void A_macro_without_a_binding_says_so()
    {
        var macro = new MacroItem();

        Assert.Equal("—", macro.TriggerPreview);
        Assert.True(macro.IsKeyboardTrigger);
    }

    [Fact]
    public void A_colour_trigger_previews_the_colour()
    {
        var macro = new MacroItem
        {
            TriggerMode = MacroTrigger.ColorPixelChanges,
            HexColor = "#4a90d9",
        };

        Assert.True(macro.IsColorTrigger);
        Assert.Equal("#4A90D9", macro.TriggerPreview);
    }

    [Fact]
    public void Changing_the_binding_refreshes_what_the_card_shows()
    {
        var macro = new MacroItem { BindKey = "NumPad7" };
        var refreshed = new List<string>();
        macro.PropertyChanged += (_, args) => refreshed.Add(args.PropertyName ?? string.Empty);

        macro.BindKey = "Mouse Left";

        Assert.Contains(nameof(MacroItem.TriggerPreview), refreshed);
        Assert.Contains(nameof(MacroItem.IsMouseTrigger), refreshed);
        Assert.Contains(nameof(MacroItem.IsKeyboardTrigger), refreshed);
    }
}
