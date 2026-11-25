using System;
using System.IO;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Projects;

namespace WhaleGenie.Tests;

/// <summary>
/// Which package a start opens. A first run has no last project; with automatic saving on it lands
/// in the default project so the first macro has a file to go to. "Start empty" is a real choice
/// and must not be undone by that fallback, and a remembered path whose file has gone stays empty.
/// </summary>
public class ProjectStartupTests
{
    [Fact]
    public void A_first_start_points_at_the_default_project_without_creating_it()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, loadLast: true, autoSave: true);
            var defaultPackage = Path.Combine(folder, "default.wgmacro");
            var project = new ProjectWorkspace();

            var opened = ProjectStartup.OpenAtStart(project, settings, TestProjectFiles.Service, defaultPackage);

            Assert.False(opened);
            Assert.Equal(defaultPackage, project.CurrentPath);
            Assert.False(File.Exists(defaultPackage));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task An_existing_default_project_is_opened()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, loadLast: true, autoSave: true);
            var defaultPackage = Path.Combine(folder, "default.wgmacro");
            var source = new ProjectWorkspace();
            source.AddMacro(new MacroItem { Name = "from-default" });
            Assert.True((await TestProjectFiles.Service.SaveAsync(source, defaultPackage)).Ok);

            var project = new ProjectWorkspace();
            var opened = ProjectStartup.OpenAtStart(project, settings, TestProjectFiles.Service, defaultPackage);

            Assert.True(opened);
            Assert.Equal(defaultPackage, project.CurrentPath);
            Assert.Contains(project.Macros, macro => macro.Name == "from-default");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task The_last_project_wins_over_the_default()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, loadLast: true, autoSave: true);
            var last = Path.Combine(folder, "last.wgmacro");
            var source = new ProjectWorkspace();
            source.AddMacro(new MacroItem { Name = "from-last" });
            Assert.True((await TestProjectFiles.Service.SaveAsync(source, last)).Ok);
            settings.StoreLastProject(last);

            var defaultPackage = Path.Combine(folder, "default.wgmacro");
            var project = new ProjectWorkspace();
            var opened = ProjectStartup.OpenAtStart(project, settings, TestProjectFiles.Service, defaultPackage);

            Assert.True(opened);
            Assert.Equal(last, project.CurrentPath);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Starting_empty_is_not_overridden_by_the_default_project()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, loadLast: false, autoSave: true);
            var project = new ProjectWorkspace();

            var opened = ProjectStartup.OpenAtStart(project, settings, TestProjectFiles.Service,
                Path.Combine(folder, "default.wgmacro"));

            Assert.False(opened);
            Assert.Null(project.CurrentPath);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void A_remembered_file_that_has_gone_is_not_replaced_by_the_default()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, loadLast: true, autoSave: true);
            settings.StoreLastProject(Path.Combine(folder, "gone.wgmacro"));
            var project = new ProjectWorkspace();

            var opened = ProjectStartup.OpenAtStart(project, settings, TestProjectFiles.Service,
                Path.Combine(folder, "default.wgmacro"));

            Assert.False(opened);
            Assert.Null(project.CurrentPath);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static AppSettings Settings(string folder, bool loadLast, bool autoSave)
    {
        var settings = new AppSettings(Path.Combine(folder, "settings.json"));
        settings.StoreLastProjectAtStart(loadLast);
        settings.StoreAutoSaveProject(autoSave);
        return settings;
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-startup-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
