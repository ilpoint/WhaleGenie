using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One public macro: the library names it, versions it and keeps its description.</summary>
[BridgeContract]
public sealed record LibraryEntry(string Id, string Name, int Version, string Description);

/// <summary>The public library as the page lists it, or the reason the file could not be read.</summary>
[BridgeContract]
public sealed record LibraryState(IReadOnlyList<LibraryEntry> Entries, string? Error);
