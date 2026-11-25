using System;
using System.IO;
using System.Threading.Tasks;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Projects;

/// <summary>The outcome a window's file operation reports to its caller.</summary>
public readonly record struct ProjectFileResult(bool Ok, Exception? Error,
    PackageProblem Problem = PackageProblem.None)
{
    public static ProjectFileResult Success() => new(true, null);

    public static ProjectFileResult Failed(Exception error,
        PackageProblem problem = PackageProblem.None) => new(false, error, problem);
}

/// <summary>
/// Opens and saves the project through <see cref="IPackageStore"/>. The workspace only holds the
/// in-memory document: preparing a copy, reading a package on a worker, applying it on the caller's
/// thread, and marking the saved revision are the application's job.
/// </summary>
public sealed class ProjectFileService(IPackageStore packages)
{
    public ProjectFileResult Open(ProjectWorkspace workspace, string path)
    {
        var read = packages.ReadAsync(path, MacroPackage.AssetFolderFor(path))
            .ConfigureAwait(false).GetAwaiter().GetResult();
        if (!read.Ok || read.Value is null)
        {
            return ProjectFileResult.Failed(read.Error ?? new InvalidDataException(read.Message),
                read.Problem);
        }

        workspace.ApplyPackage(path, read.Value);
        return ProjectFileResult.Success();
    }

    /// <summary>
    /// Opens a package without reading the file on the interface thread: the archive and its
    /// pictures are read on a worker, then the project is replaced in one turn of the caller's
    /// thread. The window and the page both open through here.
    /// </summary>
    public async Task<ProjectFileResult> OpenAsync(ProjectWorkspace workspace, string path)
    {
        var read = await packages.ReadAsync(path, MacroPackage.AssetFolderFor(path));
        if (!read.Ok || read.Value is null)
        {
            return ProjectFileResult.Failed(read.Error ?? new InvalidDataException(read.Message),
                read.Problem);
        }

        workspace.ApplyPackage(path, read.Value);
        return ProjectFileResult.Success();
    }

    public ProjectFileResult Save(ProjectWorkspace workspace, string path)
    {
        var plan = workspace.PrepareSave(path);
        var write = packages.WriteAsync(Request(plan))
            .ConfigureAwait(false).GetAwaiter().GetResult();
        if (!write.Ok)
        {
            return ProjectFileResult.Failed(write.Error ?? new IOException(write.Message),
                write.Problem);
        }

        workspace.MarkSaved(plan);
        return ProjectFileResult.Success();
    }

    /// <summary>
    /// Saves without holding the interface thread for the zip write: the plan is copied on the
    /// calling thread, the file is written on a worker, and the clean flag goes back to the caller.
    /// </summary>
    public async Task<ProjectFileResult> SaveAsync(ProjectWorkspace workspace, string path)
    {
        var plan = workspace.PrepareSave(path);
        var write = await packages.WriteAsync(Request(plan));
        if (!write.Ok)
        {
            return ProjectFileResult.Failed(write.Error ?? new IOException(write.Message),
                write.Problem);
        }

        workspace.MarkSaved(plan);
        return ProjectFileResult.Success();
    }

    /// <summary>Reads the current package again; a project with no file has nothing to read.</summary>
    public ProjectFileResult Reload(ProjectWorkspace workspace)
        => workspace.CurrentPath is { Length: > 0 } path
            ? Open(workspace, path)
            : ProjectFileResult.Failed(new InvalidOperationException("the project has no file"));

    private static PackageWriteRequest Request(ProjectSavePlan plan)
        => PackageWriteRequest.From(plan);
}
