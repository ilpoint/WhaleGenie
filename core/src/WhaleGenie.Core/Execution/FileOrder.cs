using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The order a folder listing is handed over in. One key is sorted on and the name settles every tie,
/// so two files that agree on the key still come out the same way every run; a listing asked to go
/// the other way is the whole order turned round, ties included.
/// </summary>
internal static class FileOrder
{
    public static IEnumerable<FileEntry> Of(IEnumerable<FileEntry> files, string key, bool descending)
    {
        var ordered = key.Trim().ToLowerInvariant() switch
        {
            "size" => files.OrderBy(entry => entry.Size),
            "modified" => files.OrderBy(entry => entry.Modified),
            "created" => files.OrderBy(entry => entry.Created),
            "extension" => files.OrderBy(entry => Path.GetExtension(entry.Name),
                StringComparer.OrdinalIgnoreCase),
            _ => files.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        };

        var byName = ordered.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        return descending ? byName.Reverse() : byName;
    }
}
