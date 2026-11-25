using System.Threading.Tasks;

namespace WhaleGenie.Projects;

/// <summary>Which project operation is about to replace part of the document.</summary>
public enum ProjectChangeKind
{
    /// <summary>Open a package; the current project and editor documents are being left.</summary>
    Open,

    /// <summary>Read the current package again; unsaved work is being thrown away.</summary>
    Reload,

    /// <summary>Throw the macro list away and keep an empty project.</summary>
    CloseAll,
}

/// <summary>
/// The question before a project replacement. The bridge cannot ask it itself — the page or a
/// native dialog owns the answer — so the windowed composition root supplies this port. A check
/// may leave it null, which means "replace without asking".
/// </summary>
public interface IProjectChangeGuard
{
    /// <summary>Answers true when the replacement may go ahead; false keeps the current project.</summary>
    Task<bool> ConfirmAsync(ProjectChangeKind kind);
}
