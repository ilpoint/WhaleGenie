using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Mcp;
using WhaleGenie.Models;
using WhaleGenie.Projects;

namespace WhaleGenie.Tests;

/// <summary>
/// The one writer of the package. It follows the document quietly, waits for a pause, and leaves a
/// failed write dirty instead of clearing a file it could not replace. "Save now" is what the File
/// menu and the exit question call; it works whether the automatic switch is on or not.
/// </summary>
public class ProjectSaverTests
{
    [Fact]
    public async Task A_project_replacement_cancels_the_quiet_write_meant_for_the_old_file()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, autoSave: true);
            var oldPackage = Path.Combine(folder, "old.wgmacro");
            var newPackage = Path.Combine(folder, "new.wgmacro");
            var project = new ProjectWorkspace { CurrentPath = oldPackage };
            using var auto = new ProjectSaver(project, settings, DirectUiDispatcher.Instance, TestProjectFiles.Service,
                TimeSpan.FromMilliseconds(50));

            project.AddMacro(new MacroItem { Name = "old" });
            var replaced = await auto.ReplaceAsync(() =>
            {
                project.ClearMacros();
                project.CurrentPath = newPackage;
                project.IsDirty = false;
                return Task.FromResult(true);
            });

            await Task.Delay(150);
            Assert.True(replaced);
            Assert.False(File.Exists(oldPackage),
                "a write queued for the old project landed after the replacement");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_project_replacement_resumes_saving_for_the_new_document()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, autoSave: true);
            var newPackage = Path.Combine(folder, "new.wgmacro");
            var project = new ProjectWorkspace { CurrentPath = Path.Combine(folder, "old.wgmacro") };
            using var auto = new ProjectSaver(project, settings, DirectUiDispatcher.Instance, TestProjectFiles.Service,
                TimeSpan.FromMilliseconds(10));

            await auto.ReplaceAsync(() =>
            {
                project.ClearMacros();
                project.AddMacro(new MacroItem { Name = "new" });
                project.CurrentPath = newPackage;
                return Task.FromResult(true);
            });

            await Until(() => File.Exists(newPackage) && !project.IsDirty);
            var reopened = new ProjectWorkspace();
            Assert.True(TestProjectFiles.Service.Open(reopened, newPackage).Ok);
            Assert.Equal("new", reopened.Macros.Single().Name);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_change_is_written_after_the_quiet_period()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, autoSave: true);
            var package = Path.Combine(folder, "macros.wgmacro");
            var project = new ProjectWorkspace { CurrentPath = package };
            using var auto = new ProjectSaver(project, settings, DirectUiDispatcher.Instance, TestProjectFiles.Service,
                TimeSpan.FromMilliseconds(10));

            project.AddMacro(new MacroItem { Name = "kept" });

            await Until(() => File.Exists(package) && !project.IsDirty);

            // The quiet write remembers the package in the settings file as well, after the package
            // itself; waiting for it is what keeps the folder removable on a loaded machine, where
            // the last write can still be running when the test is over.
            await auto.FlushAsync();
            Assert.Equal(package, project.CurrentPath);
            Assert.Null(auto.Error);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Editing_a_macro_does_not_write_until_the_macro_is_saved()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, autoSave: true);
            var package = Path.Combine(folder, "macros.wgmacro");
            var project = new ProjectWorkspace { CurrentPath = package };
            project.AddMacro(new MacroItem { Name = "one", Description = "before" });
            Assert.True((await TestProjectFiles.Service.SaveAsync(project, package)).Ok);
            using var auto = new ProjectSaver(project, settings, DirectUiDispatcher.Instance, TestProjectFiles.Service,
                TimeSpan.FromMilliseconds(10));
            var documents = new EditorDocumentSet(project);
            var session = documents.Open("one");

            session.Patch(description: "edited but not saved");
            await Task.Delay(100);

            // The editor's own dirty state is not the project's: nothing was written and the
            // workspace still holds the version it had.
            Assert.False(project.IsDirty);
            Assert.Equal("before", project.Macros.Single().Description);
            Assert.True(session.IsDirty);

            Assert.True(session.Save().Succeeded);
            await Until(() => File.Exists(package) && !project.IsDirty);

            var reopened = new ProjectWorkspace();
            Assert.True(TestProjectFiles.Service.Open(reopened, package).Ok);
            Assert.Equal("edited but not saved", reopened.Macros.Single().Description);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Turning_the_switch_off_leaves_the_change_unsaved()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, autoSave: false);
            var package = Path.Combine(folder, "macros.wgmacro");
            var project = new ProjectWorkspace { CurrentPath = package };
            using var auto = new ProjectSaver(project, settings, DirectUiDispatcher.Instance, TestProjectFiles.Service,
                TimeSpan.FromMilliseconds(10));

            project.AddMacro(new MacroItem { Name = "quiet" });
            await Task.Delay(100);

            Assert.False(File.Exists(package));
            Assert.True(project.IsDirty);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task Save_now_writes_even_when_automatic_saving_is_off()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, autoSave: false);
            var package = Path.Combine(folder, "macros.wgmacro");
            var project = new ProjectWorkspace { CurrentPath = package };
            using var auto = new ProjectSaver(project, settings, DirectUiDispatcher.Instance, TestProjectFiles.Service,
                TimeSpan.FromMilliseconds(10));

            project.AddMacro(new MacroItem { Name = "manual" });
            var result = await auto.SaveAsync();

            Assert.True(result.Ok);
            Assert.True(File.Exists(package));
            Assert.False(project.IsDirty);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public async Task A_failed_write_keeps_the_work_unsaved_and_says_why()
    {
        var folder = Folder();
        try
        {
            var settings = Settings(folder, autoSave: true);
            var project = new ProjectWorkspace { CurrentPath = folder };
            using var auto = new ProjectSaver(project, settings, DirectUiDispatcher.Instance, TestProjectFiles.Service,
                TimeSpan.FromMilliseconds(10));

            project.AddMacro(new MacroItem { Name = "stuck" });
            var result = await auto.SaveAsync();

            Assert.False(result.Ok);
            Assert.True(project.IsDirty);
            Assert.False(string.IsNullOrWhiteSpace(auto.Error));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private static AppSettings Settings(string folder, bool autoSave)
    {
        var settings = new AppSettings(Path.Combine(folder, "settings.json"));
        settings.StoreAutoSaveProject(autoSave);
        return settings;
    }

    private static string Folder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-autosave-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("the automatic save did not arrive");
            }

            await Task.Delay(10);
        }
    }
}
