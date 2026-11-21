using System;
using WhaleGenie.Models;

namespace WhaleGenie.Projects;

/// <summary>
/// The one place the package file is opened or written. The bridge and the native window differ in
/// how they ask the person for a path and how they report a failure; the file operation itself is
/// the same, and belongs in one place.
/// </summary>
public readonly record struct ProjectFileResult(bool Ok, Exception? Error)
{
    public static ProjectFileResult Success() => new(true, null);

    public static ProjectFileResult Failed(Exception error) => new(false, error);
}

public static class ProjectFiles
{
    public static ProjectFileResult Open(ProjectWorkspace workspace, string path)
    {
        try
        {
            workspace.OpenPackage(path);
            return ProjectFileResult.Success();
        }
        catch (Exception error)
        {
            return ProjectFileResult.Failed(error);
        }
    }

    public static ProjectFileResult Save(ProjectWorkspace workspace, string path)
    {
        try
        {
            workspace.SavePackage(path);
            return ProjectFileResult.Success();
        }
        catch (Exception error)
        {
            return ProjectFileResult.Failed(error);
        }
    }

    /// <summary>Reads the current package again; a project with no file has nothing to read.</summary>
    public static ProjectFileResult Reload(ProjectWorkspace workspace)
        => workspace.CurrentPath is { Length: > 0 } path
            ? Open(workspace, path)
            : ProjectFileResult.Failed(new InvalidOperationException("the project has no file"));
}
