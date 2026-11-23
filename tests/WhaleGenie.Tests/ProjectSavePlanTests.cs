using System.IO;
using System.Linq;
using WhaleGenie.Models;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// A save is copied off the live project on the interface thread, so the file can be written on a
/// worker without reading objects the window is still editing.
/// </summary>
public class ProjectSavePlanTests
{
    [Fact]
    public void A_plan_is_a_copy_and_not_a_window_into_the_project()
    {
        var project = new ProjectWorkspace
        {
            CurrentPath = Path.Combine(Path.GetTempPath(), "plan.wgmacro"),
        };
        project.AddMacro(new MacroItem { Name = "one" });

        var plan = Assert.IsType<ProjectSavePlan>(project.PrepareSave());
        project.Macros[0].Name = "two";

        Assert.Equal("one", Assert.Single(plan.Macros).Name);
        Assert.Equal(project.CurrentPath, plan.Path);
    }

    [Fact]
    public void A_project_with_no_file_has_nothing_to_plan()
        => Assert.Null(new ProjectWorkspace().PrepareSave());

    [Fact]
    public void Marking_saved_records_the_path_and_clears_the_dirty_flag()
    {
        var project = new ProjectWorkspace();
        project.AddMacro(new MacroItem { Name = "one" });
        Assert.True(project.IsDirty);

        project.MarkSaved(Path.Combine(Path.GetTempPath(), "saved.wgmacro"));

        Assert.False(project.IsDirty);
        Assert.EndsWith("saved.wgmacro", project.CurrentPath ?? "", System.StringComparison.Ordinal);
    }
}
