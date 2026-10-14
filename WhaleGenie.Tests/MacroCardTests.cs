using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

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

    /// <summary>
    /// A trigger bound to a combination is still a keyboard trigger: nothing in its name says
    /// "mouse", so the card keeps the keyboard icon and previews the combination as it was bound.
    /// </summary>
    [Fact]
    public void A_combination_binding_is_previewed_with_the_keyboard_icon()
    {
        var macro = new MacroItem { BindKey = "Ctrl+Shift+S" };

        Assert.True(macro.IsKeyboardTrigger);
        Assert.False(macro.IsMouseTrigger);
        Assert.Equal("Ctrl+Shift+S", macro.TriggerPreview);
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
        // A card with nothing written on it would read as a macro that starts some other way, so a
        // trigger with nothing to preview says "none" in the reader's own language.
        var macro = new MacroItem();

        Assert.Equal(Strings.Get("Common.None"), macro.TriggerPreview);
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
