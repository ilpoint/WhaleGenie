using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Domain.Tests;

/// <summary>
/// What a macro card shows about the triggers that point at it. The card holds no triggers of its
/// own: the project writes these lines from the entries, which are the only ones there are.
/// </summary>
public class MacroCardTests
{
    [Fact]
    public void A_key_entry_is_previewed_with_its_binding()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry { MacroName = "m", Spec = new KeySpec("右Shift") });
        var macro = new MacroItem { Name = "m" };

        triggers.Project([macro]);

        Assert.Equal("右Shift", macro.CardTriggerPreview);
        Assert.Equal("右Shift", macro.CardTriggerSummary);
        Assert.Equal(Strings.Get("Loop.Press"), macro.CardTriggerActions);
    }

    [Fact]
    public void A_colour_entry_previews_the_colour()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "m",
            Spec = new ColorSpec(Hex: "#4a90d9"),
        });
        var macro = new MacroItem { Name = "m" };

        triggers.Project([macro]);

        Assert.Equal("#4A90D9", macro.CardTriggerPreview);
    }

    [Fact]
    public void A_macro_with_no_trigger_says_so()
    {
        var triggers = new ProjectTriggers();
        var macro = new MacroItem { Name = "m" };

        triggers.Project([macro]);

        Assert.Equal(Strings.Get("Trigger.Unset"), macro.CardTriggerSummary);
        Assert.Equal(Strings.Get("Common.None"), macro.CardTriggerPreview);
        Assert.Empty(macro.CardTriggerActions);
    }

    /// <summary>
    /// A key and a colour say how they repeat; a schedule starts the macro rather than holding it,
    /// and a menu cell, a floating group and a typed abbreviation have nothing to repeat.
    /// </summary>
    [Fact]
    public void Only_key_and_colour_entries_show_loop_wording()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "m",
            Spec = new KeySpec("F8", MacroLoop.Toggle),
        });
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "m",
            Spec = new ColorSpec(Loop: MacroLoop.Press),
        });
        triggers.Entries.Add(new TriggerEntry
        {
            MacroName = "m",
            Spec = new InterfaceSpec(Core.Devices.WindowRule.Empty),
        });
        triggers.Entries.Add(new TriggerEntry { MacroName = "m", Spec = new HotstringSpec("sig") });
        triggers.Entries.Add(new TriggerEntry { MacroName = "m", Spec = new MenuSpec() });
        var macro = new MacroItem { Name = "m" };

        triggers.Project([macro]);

        Assert.Equal($"{Strings.Get("Loop.Toggle")} · {Strings.Get("Loop.Press")}",
            macro.CardTriggerActions);
    }

    [Fact]
    public void Projecting_again_follows_a_changed_entry()
    {
        var triggers = new ProjectTriggers();
        var entry = new TriggerEntry { MacroName = "m", Spec = new KeySpec("NumPad7") };
        triggers.Entries.Add(entry);
        var macro = new MacroItem { Name = "m" };
        triggers.Project([macro]);

        var refreshed = new List<string>();
        macro.PropertyChanged += (_, args) => refreshed.Add(args.PropertyName ?? string.Empty);

        entry.Spec = new KeySpec("F9");
        triggers.Project([macro]);

        Assert.Equal("F9", macro.CardTriggerPreview);
        Assert.Contains(nameof(MacroItem.CardTriggerPreview), refreshed);
    }
}
