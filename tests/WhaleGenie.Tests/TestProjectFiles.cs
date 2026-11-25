using System.Threading.Tasks;
using WhaleGenie.Models;
using WhaleGenie.Projects;
using WhaleGenie.Storage;

namespace WhaleGenie.Tests;

/// <summary>
/// The real file service over temporary test packages. It is one stateless object for the test
/// suite; the service itself has no state, only the store's path argument decides what is touched.
/// </summary>
internal static class TestProjectFiles
{
    public static ProjectFileService Service { get; } = new(new FilePackageStore());

    public static PackageTransferService Transfer { get; } = new(
        new FilePackageStore(), EmptyPublicLibraryStore.Instance);

    public static ProjectFileResult Save(ProjectWorkspace workspace, string path)
        => Service.Save(workspace, path);

    public static Task<ProjectFileResult> SaveAsync(ProjectWorkspace workspace, string path)
        => Service.SaveAsync(workspace, path);

    public static ProjectFileResult Open(ProjectWorkspace workspace, string path)
        => Service.Open(workspace, path);

    public static Task<ProjectFileResult> OpenAsync(ProjectWorkspace workspace, string path)
        => Service.OpenAsync(workspace, path);
}
