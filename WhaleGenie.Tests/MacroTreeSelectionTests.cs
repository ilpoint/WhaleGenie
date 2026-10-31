using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// What a tick in the export window means. A macro carries what is written inside it; a submacro
/// can leave on its own; and a macro that is ticked covers its rows so the same steps cannot be
/// counted twice.
/// </summary>
public class MacroTreeSelectionTests
{
    [Fact]
    public void A_ticked_macro_takes_its_submacros_with_it()
    {
        var macro = WithSubs("整理", "读表", "收尾");
        var selection = new MacroTreeSelection([macro]);

        var root = selection.Roots[0];
        selection.SetChecked(root, true);

        var chosen = Assert.Single(selection.Chosen);
        Assert.Same(macro, chosen);

        // The rows are ticked so the file matches what is on screen, but they are not taken again.
        Assert.All(root.Children, child => Assert.True(child.IsChecked));
        Assert.All(root.Children, child => Assert.True(child.IsCovered));
        Assert.All(root.Children, child => Assert.False(child.TakesPart));
    }

    [Fact]
    public void A_ticked_submacro_leaves_on_its_own()
    {
        var macro = WithSubs("整理", "读表");
        var selection = new MacroTreeSelection([macro]);

        selection.SetChecked(selection.Roots[0].Children[0], true);

        var chosen = Assert.Single(selection.Chosen);
        Assert.Equal("读表", chosen.Name);
        Assert.False(selection.Roots[0].IsChecked);
    }

    [Fact]
    public void Unticking_a_macro_hands_its_submacros_back()
    {
        var macro = WithSubs("整理", "读表");
        var selection = new MacroTreeSelection([macro]);
        var root = selection.Roots[0];

        selection.SetChecked(root, true);
        selection.SetChecked(root, false);

        Assert.Empty(selection.Chosen);
        Assert.All(root.Children, child => Assert.False(child.IsChecked));
        Assert.All(root.Children, child => Assert.False(child.IsCovered));
    }

    [Fact]
    public void All_picks_every_macro_and_nothing_inside_them()
    {
        var one = WithSubs("整理", "读表");
        var two = new MacroItem { Name = "收尾" };
        var selection = new MacroTreeSelection([one, two]);

        selection.SetAll(true);

        Assert.Equal(2, selection.Chosen.Count);
        Assert.Contains(selection.Chosen, macro => macro.Name == "整理");
        Assert.Contains(selection.Chosen, macro => macro.Name == "收尾");
        Assert.DoesNotContain(selection.Chosen, macro => macro.Name == "读表");
    }

    /// <summary>A card's own menu opens the window with that macro already ticked.</summary>
    [Fact]
    public void The_window_can_open_with_a_macro_already_ticked()
    {
        var one = WithSubs("整理", "读表");
        var two = new MacroItem { Name = "收尾" };
        var selection = new MacroTreeSelection([one, two], [two]);

        var chosen = Assert.Single(selection.Chosen);
        Assert.Same(two, chosen);
        Assert.True(selection.Roots[0].Children[0].IsCovered == false);
    }

    /// <summary>
    /// Where an import goes is decided by what was ticked, not by a second question: a macro joins
    /// the project, and submacros chosen on their own go to the public library.
    /// </summary>
    [Fact]
    public void Ticking_only_submacros_means_the_public_library()
    {
        var macro = WithSubs("整理", "读表");
        var selection = new MacroTreeSelection([macro]);

        selection.SetChecked(selection.Roots[0], true);
        Assert.False(selection.OnlySubMacros);

        selection.SetChecked(selection.Roots[0], false);
        selection.SetChecked(selection.Roots[0].Children[0], true);
        Assert.True(selection.OnlySubMacros);

        // A macro ticked beside a submacro is still a project import.
        selection.SetChecked(selection.Roots[0], true);
        Assert.False(selection.OnlySubMacros);
    }

    private static MacroItem WithSubs(string name, params string[] subs)
    {
        var macro = new MacroItem { Name = name };
        foreach (var sub in subs)
        {
            macro.SubMacros.Add(new MacroItem { Name = sub });
        }

        return macro;
    }
}
