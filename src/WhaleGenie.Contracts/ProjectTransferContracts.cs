using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>What one row of the transfer wizard shows: a macro in the file or in the project.</summary>
/// <remarks>
/// <paramref name="Kind"/> is the localized trigger kind the macro answers to, and
/// <paramref name="Problem"/> the first declaration fault that would stop the package writer. Both
/// are words the program already owns, because a page that assembles them itself would be a second
/// translator of the same fact.
/// </remarks>
[BridgeContract]
public sealed record TransferMacro(
    string Name, string Kind, int Steps, string Problem, bool IsSub);

/// <summary>One global variable the file declares, and whether the project already has the name.</summary>
[BridgeContract]
public sealed record TransferGlobal(string Name, string Type, bool Conflicts);

/// <summary>What an import would bring, before any of it is written.</summary>
[BridgeContract]
public sealed record ProjectImportPlan(
    string Source,
    string FileName,
    IReadOnlyList<TransferMacro> Macros,
    IReadOnlyList<TransferGlobal> Globals);

/// <summary>The answer to one global-variable conflict the import asked about.</summary>
/// <remarks>
/// The three words are <c>keep</c> (use the project's declaration), <c>overwrite</c> (replace it
/// with the file's) and <c>rename</c> (give the arriving one a name of its own). The same list is
/// <see cref="ImportActions"/>; a decision for a name that is not in conflict is ignored.
/// </remarks>
[BridgeContract]
public sealed record ImportDecision(string Name, string Action);

/// <summary>What an import did, in words, and the state the list should now draw.</summary>
[BridgeContract]
public sealed record ImportResult(string Report, ProjectState State);

/// <summary>What an export could hold: every macro of the project, submacros included.</summary>
[BridgeContract]
public sealed record ProjectExportPlan(string SuggestedName, IReadOnlyList<TransferMacro> Macros);

/// <summary>Where an export was written.</summary>
[BridgeContract]
public sealed record ExportResult(string Path);

/// <summary>The actions an <see cref="ImportDecision"/> may name.</summary>
public static class ImportActions
{
    public const string Keep = "keep";
    public const string Overwrite = "overwrite";
    public const string Rename = "rename";
}
