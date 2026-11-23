using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;
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
    public async Task An_async_save_writes_the_package_and_clears_the_dirty_flag()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-save-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var project = new ProjectWorkspace();
            project.AddMacro(new MacroItem { Name = "one" });
            var path = Path.Combine(folder, "macros" + MacroPackage.Extension);

            var saved = await ProjectFiles.SaveAsync(project, path);

            Assert.True(saved.Ok);
            Assert.True(File.Exists(path));
            Assert.Equal(path, project.CurrentPath);
            Assert.False(project.IsDirty);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task An_async_open_reads_the_package_and_replaces_the_project()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-open-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "macros" + MacroPackage.Extension);
            var source = new ProjectWorkspace();
            source.AddMacro(new MacroItem { Name = "one" });
            Assert.True((await ProjectFiles.SaveAsync(source, path)).Ok);

            var target = new ProjectWorkspace();
            target.AddMacro(new MacroItem { Name = "old" });
            var opened = await ProjectFiles.OpenAsync(target, path);

            Assert.True(opened.Ok);
            Assert.Equal(path, target.CurrentPath);
            Assert.False(target.IsDirty);
            Assert.Contains(target.Macros, macro => macro.Name == "one");
            Assert.DoesNotContain(target.Macros, macro => macro.Name == "old");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
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
