using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Projects;

/// <summary>The kind of failure a package read or write can end with, without naming a window.</summary>
public enum PackageProblem
{
    None,
    NotFound,
    Unreadable,
    InvalidFormat,
    LibraryNotProject,
    WriteFailed,
    AccessDenied,
    UnknownMacro,
    UnknownAction,
    ImportFailed,
}

/// <summary>
/// The result of a package operation. The bridge, MCP and the file list each translate the problem
/// into their own words; the package layer never throws a bridge error.
/// </summary>
public sealed record PackageOutcome(
    bool Ok,
    PackageProblem Problem,
    string? Message,
    Exception? Error)
{
    public static PackageOutcome Success() => new(true, PackageProblem.None, null, null);

    public static PackageOutcome Failed(PackageProblem problem, string message, Exception? error = null)
        => new(false, problem, message, error);
}

/// <summary>The same result when the operation produced something worth keeping.</summary>
public sealed record PackageOutcome<T>(
    bool Ok,
    T? Value,
    PackageProblem Problem,
    string? Message,
    Exception? Error);

/// <summary>Constructors for the generic result, kept off the generic type because CA1000 reads a
/// static member there as a shape the caller has to qualify with a type argument.</summary>
public static class PackageOutcomes
{
    public static PackageOutcome<T> Success<T>(T value)
        => new(true, value, PackageProblem.None, null, null);

    public static PackageOutcome<T> Failed<T>(PackageProblem problem, string message,
        Exception? error = null)
        => new(false, default, problem, message, error);
}

/// <summary>What one package write needs; plain data, so it can be handed to a worker.</summary>
public sealed record PackageWriteRequest(
    string Path,
    IReadOnlyList<MacroItem> Macros,
    ProjectTriggers Triggers,
    IReadOnlyList<VariableDefinition> Globals,
    string AppVersion,
    string? AssetFolder)
{
    public static PackageWriteRequest From(ProjectSavePlan plan)
        => new(plan.Path, plan.Macros, plan.Triggers, plan.Globals, plan.AppVersion,
            plan.AssetFolder);
}

/// <summary>
/// The package file boundary. It reads and writes the one archive format and keeps the window, the
/// page and MCP from owning file-system details. Atomic replacement and the previous-file backup
/// belong inside the implementation, not in each caller.
/// </summary>
public interface IPackageStore
{
    /// <summary>Reads a package and unpacks its pictures into <paramref name="assetFolder"/>.</summary>
    Task<PackageOutcome<PackageContents>> ReadAsync(string path, string assetFolder,
        CancellationToken cancellationToken = default);

    /// <summary>Writes the archive atomically, preserving the previous package as a backup.</summary>
    Task<PackageOutcome> WriteAsync(PackageWriteRequest request,
        CancellationToken cancellationToken = default);
}
