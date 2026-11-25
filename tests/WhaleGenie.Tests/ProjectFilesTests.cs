using System;
using System.IO;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The package operation behind both the native file menu and the page's project commands. A
/// failure comes back as a value with its exception, so each caller can report it in its own words
/// without a second copy of the file logic.
/// </summary>
public class ProjectFilesTests
{
    [Fact]
    public void A_project_round_trips_through_one_operation()
    {
        var path = Path.Combine(Path.GetTempPath(), $"whalegenie-project-{Guid.NewGuid():N}.wgmacro");
        try
        {
            var workspace = new ProjectWorkspace();
            workspace.AddMacro(new MacroItem { Name = "saved" });

            var saved = TestProjectFiles.Service.Save(workspace, path);
            Assert.True(saved.Ok, saved.Error?.Message);
            Assert.True(File.Exists(path));

            var loaded = new ProjectWorkspace();
            var opened = TestProjectFiles.Service.Open(loaded, path);
            Assert.True(opened.Ok, opened.Error?.Message);
            Assert.Equal("saved", Assert.Single(loaded.Macros).Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Embedded_library_entries_travel_with_the_project()
    {
        var path = Path.Combine(Path.GetTempPath(), $"whalegenie-project-{Guid.NewGuid():N}.wgmacro");
        try
        {
            var workspace = new ProjectWorkspace();
            workspace.AddMacro(new MacroItem { Name = "saved" });
            workspace.MergeLibrary(
                [new PublicMacroEntry("public-id", 3, new MacroItem { Name = "shared" })]);

            var saved = TestProjectFiles.Service.Save(workspace, path);
            Assert.True(saved.Ok, saved.Error?.Message);

            var loaded = new ProjectWorkspace();
            var opened = TestProjectFiles.Service.Open(loaded, path);
            Assert.True(opened.Ok, opened.Error?.Message);
            var shared = Assert.Single(loaded.Library);
            Assert.Equal("public-id", shared.Id);
            Assert.Equal(3, shared.Version);
            Assert.Equal("shared", shared.Macro.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_library_package_is_not_an_engineering_package()
    {
        var path = Path.Combine(Path.GetTempPath(), $"whalegenie-library-{Guid.NewGuid():N}.wgmacro");
        try
        {
            PublicMacroLibrary.Save(path,
                [new PublicMacroEntry("public-id", 1, new MacroItem { Name = "shared" })], "test");

            var result = TestProjectFiles.Service.Open(new ProjectWorkspace(), path);

            Assert.False(result.Ok);
            Assert.Equal(PackageProblem.LibraryNotProject, result.Problem);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_missing_package_is_a_result_not_an_exception()
    {
        var result = TestProjectFiles.Service.Open(new ProjectWorkspace(),
            Path.Combine(Path.GetTempPath(), $"whalegenie-missing-{Guid.NewGuid():N}.wgmacro"));

        Assert.False(result.Ok);
        Assert.NotNull(result.Error);
    }
}
