using System.Collections.Generic;
using System.Threading.Tasks;

namespace WhaleGenie.Contracts;

/// <summary>A package operation that failed after its path was known, so the caller can name it.</summary>
public sealed record ProjectOperationFailure(string Path, string Message);

/// <summary>The native import/export choosers and conflict windows behind the project bridge.</summary>
public interface IProjectInteraction
{
    Task<ProjectOperationFailure?> ImportAsync();

    Task<ProjectOperationFailure?> ExportAsync(IReadOnlyList<string> macroNames);
}
