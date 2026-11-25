using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Projects;

/// <summary>
/// The real package files. The codec stays in <see cref="MacroPackage"/>; this class owns the
/// boundary around it: I/O on a worker, one error vocabulary, and the promise that callers never
/// see a raw <see cref="IOException"/> as a bridge message.
/// </summary>
public sealed class FilePackageStore : IPackageStore
{
    public async Task<PackageOutcome<PackageContents>> ReadAsync(string path, string assetFolder,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var contents = await Task.Run(
                () => MacroPackage.Load(path, assetFolder), cancellationToken)
                .ConfigureAwait(false);
            return PackageOutcomes.Success(contents);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return PackageOutcomes.Failed<PackageContents>(ProblemForRead(error),
                error.Message, error);
        }
    }

    public async Task<PackageOutcome<PackageContents>> PreviewAsync(string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var contents = await Task.Run(() => MacroPackage.Preview(path), cancellationToken)
                .ConfigureAwait(false);
            return PackageOutcomes.Success(contents);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return PackageOutcomes.Failed<PackageContents>(ProblemForRead(error),
                error.Message, error);
        }
    }

    public async Task<PackageOutcome> WriteAsync(PackageWriteRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => MacroPackage.Save(request.Path, request.Macros,
                request.Triggers, request.Globals, request.AppVersion, request.AssetFolder,
                request.Library), cancellationToken).ConfigureAwait(false);
            return PackageOutcome.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return PackageOutcome.Failed(ProblemForWrite(error), error.Message, error);
        }
    }

    private static PackageProblem ProblemForRead(Exception error) => error switch
    {
        PackageKindMismatchException { Actual: PackageKind.Library } => PackageProblem.LibraryNotProject,
        PackageKindMismatchException => PackageProblem.InvalidFormat,
        FileNotFoundException or DirectoryNotFoundException => PackageProblem.NotFound,
        InvalidDataException => PackageProblem.InvalidFormat,
        UnauthorizedAccessException => PackageProblem.AccessDenied,
        IOException => PackageProblem.Unreadable,
        _ => PackageProblem.Unreadable,
    };

    private static PackageProblem ProblemForWrite(Exception error) => error switch
    {
        UnauthorizedAccessException => PackageProblem.AccessDenied,
        IOException => PackageProblem.WriteFailed,
        InvalidDataException => PackageProblem.InvalidFormat,
        _ => PackageProblem.WriteFailed,
    };
}
