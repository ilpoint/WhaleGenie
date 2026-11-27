using WhaleGenie.Models;

namespace WhaleGenie.Domain.Tests;

/// <summary>The macro order belongs to the project document, not to the page's current filter.</summary>
public class ProjectWorkspaceReorderTests
{
    [Fact]
    public void Reordering_moves_the_macros_and_marks_the_project_changed()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "一" });
        workspace.AddMacro(new MacroItem { Name = "二" });
        workspace.AddMacro(new MacroItem { Name = "三" });
        var revision = workspace.Revision;

        var changed = workspace.ReorderMacros(["三", "一", "二"]);

        Assert.True(changed);
        Assert.Equal(["三", "一", "二"], workspace.Macros.Select(macro => macro.Name));
        Assert.True(workspace.IsDirty);
        Assert.True(workspace.Revision > revision);
    }

    [Fact]
    public void A_duplicate_or_foreign_name_leaves_the_order_alone()
    {
        var workspace = new ProjectWorkspace();
        workspace.AddMacro(new MacroItem { Name = "一" });
        workspace.AddMacro(new MacroItem { Name = "二" });

        Assert.False(workspace.ReorderMacros(["一", "一"]));
        Assert.False(workspace.ReorderMacros(["一", "别处"]));
        Assert.Equal(["一", "二"], workspace.Macros.Select(macro => macro.Name));
    }
}
