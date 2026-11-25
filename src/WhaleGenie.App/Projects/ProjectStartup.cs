using System.IO;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Projects;

/// <summary>
/// Which package a start opens, or which one it is about to write. The answer is one place so the
/// window, a check and the first-run explanation cannot disagree about what "no project yet" means.
/// </summary>
public static class ProjectStartup
{
    /// <summary>
    /// Opens the previous package when it is there, or points a first start at the default project.
    /// Answers whether a package was opened (as opposed to merely pointed at). A remembered path
    /// whose file has gone is left empty rather than replaced by the default: the person may have
    /// moved a drive, and quietly pointing them at another project is a worse answer than an empty
    /// list with their path remembered.
    /// </summary>
    public static bool OpenAtStart(ProjectWorkspace workspace, AppSettings settings,
        ProjectFileService files, string? defaultProject = null)
    {
        var wantedDefault = defaultProject ?? AppPaths.DefaultProject;
        var last = settings.LastProject();

        if (settings.LoadLastProjectAtStart()
            && last is { Length: > 0 }
            && File.Exists(last))
        {
            return files.Open(workspace, last).Ok;
        }

        // "Start empty" is a real choice: it must not be undone by the default project.
        if (!settings.LoadLastProjectAtStart() || last is { Length: > 0 })
        {
            return false;
        }

        if (!settings.LoadAutoSaveProject())
        {
            return false;
        }

        if (File.Exists(wantedDefault))
        {
            return files.Open(workspace, wantedDefault).Ok;
        }

        // No file yet: the path is where the first change will be written. Nothing is created on
        // disk until there is something worth keeping.
        workspace.CurrentPath = wantedDefault;
        return false;
    }
}
