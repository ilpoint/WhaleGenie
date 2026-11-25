using WhaleGenie.Editing;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// Removing a child has a pure question before the list mutation: does the child exist and is it
/// still called? The caller can ask it before committing to the edit.
/// </summary>
public class SubMacroRulesTests
{
    [Fact]
    public void A_child_can_be_removed_only_after_its_last_call_is_gone()
    {
        var owner = new MacroItem { Name = "owner" };
        var child = new MacroItem { Name = "child" };
        owner.SubMacros.Add(child);

        Assert.True(SubMacroEditor.CanDelete(owner, "child"));

        owner.Steps.Add(SubMacroEditor.CallStep(child));

        Assert.False(SubMacroEditor.CanDelete(owner, "child"));
        Assert.False(SubMacroEditor.Delete(owner, "child"));
        Assert.Single(owner.SubMacros);

        owner.Steps.Clear();

        Assert.True(SubMacroEditor.Delete(owner, "child"));
        Assert.Empty(owner.SubMacros);
    }

    [Fact]
    public void An_unknown_child_is_not_removable()
    {
        var owner = new MacroItem { Name = "owner" };

        Assert.False(SubMacroEditor.CanDelete(owner, "missing"));
        Assert.False(SubMacroEditor.Delete(owner, "missing"));
    }
}
