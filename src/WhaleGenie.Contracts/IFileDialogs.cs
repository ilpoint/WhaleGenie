using System.Threading.Tasks;

namespace WhaleGenie.Contracts;

/// <summary>The files a page action asks the person to choose; the UI project owns the windows.</summary>
public interface IFileDialogs
{
    Task<string?> OpenPackageAsync();

    Task<string?> SavePackageAsync(string suggestedName);

    Task<string?> OpenImageAsync();
}
