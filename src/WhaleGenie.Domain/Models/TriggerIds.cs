using System;

namespace WhaleGenie.Models;

/// <summary>
/// The stable names a trigger entry goes by. The runtime keeps what it has seen per entry — an
/// edge, a due moment, a baseline — so the name has to survive editing the entry, copying it into
/// another project and saving the package. A position in a list does not; that is why this is not
/// a counter.
/// </summary>
public static class TriggerIds
{
    /// <summary>A new name, short enough to read in a saved file and wide enough not to collide
    /// when two projects are merged.</summary>
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
