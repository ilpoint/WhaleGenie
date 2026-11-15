using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Folders, archives and listings: making and removing them, zipping, and the order of what is found.
public sealed partial class MacroRunner
{
    private void CreateFolder(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        _devices.Files.CreateFolder(path);
        Log(LogLevel.Info, depth, step.Type, LogKeys.MadeFolder, path);
    }

    private void DeleteFolder(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var recurse = Flag(step, "recurse", false);

        _devices.Files.DeleteFolder(path, recurse);
        Log(LogLevel.Info, depth, step.Type, LogKeys.RemovedFolder, path);
    }

    /// <summary>
    /// Works out one piece of a path. A macro that has to say "the file next to this one" or "the
    /// same name with .bak on the end" should not have to spell the whole path out again, and the
    /// two folder answers are the ones that are always in the same place on any machine.
    /// </summary>
    private void PathPart(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var other = Args(step).ValueOf("fileName").AsText().Trim();

        var answer = Args(step).Text("operation").Trim().ToLowerInvariant() switch
        {
            "folder" => Path.GetDirectoryName(path) ?? string.Empty,
            "name" => Path.GetFileName(path),
            "basename" => Path.GetFileNameWithoutExtension(path),
            "extension" => Path.GetExtension(path),
            "full" => _devices.Files.Resolve(path),
            "temp" => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            "macros" => _devices.Files.BaseFolder,
            _ => other.Length == 0 ? path : Path.Combine(path, other),
        };

        Store(step, "path", Value.FromText(answer), depth);
    }

    private void UnzipFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var folder = PathOf(step, "folder");
        var overwrite = Flag(step, "overwrite", true);

        _devices.Files.Unzip(from, folder, overwrite);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Unzipped, from, folder);
    }

    private void ZipFolder(ExecutableStep step, int depth)
    {
        var folder = PathOf(step, "folder");
        var to = PathOf(step, "to");
        var includeTopFolder = Flag(step, "includeTopFolder", false);

        _devices.Files.Zip(folder, to, includeTopFolder);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Zipped, folder, to);
    }

    private void ListFiles(ExecutableStep step, int depth)
    {
        var folder = PathOf(step, "folder");
        var recurse = Flag(step, "recurse", false);
        // A filter said with a semi-colon is several filters, the way the action this is modelled
        // on reads it: *.txt; *.csv is one question, not two steps.
        var patterns = Args(step).Text("pattern")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var levels = Args(step).Number("depth");
        // 收文件还是收文件夹：默认收文件，跟这个选择出现之前一样。
        var kind = Args(step).Text("kind").Trim().ToLowerInvariant() switch
        {
            "folders" => ListKind.Folders,
            "both" => ListKind.Both,
            _ => ListKind.Files,
        };
        var files = _devices.Files.List(folder, patterns, recurse,
            recurse ? (levels <= 0 ? -1 : levels) : 0, kind);

        var since = ChangedSince(step);
        var smallest = Args(step).Number("minKb") * 1024L;
        var largest = Args(step).Number("maxKb") * 1024L;
        var kept = files.Where(entry =>
            (since is null || entry.Modified >= since)
            && (smallest <= 0 || entry.Size >= smallest)
            && (largest <= 0 || entry.Size <= largest));

        // A listing a macro loops over is the same listing on every run, so the folder's own order
        // is never the answer: an order is always asked for, and the name settles the ties.
        var ordered = InOrder(kept, Args(step).Text("sortBy").Trim(), Flag(step, "descending", false));
        var root = Flag(step, "relative", false) ? _devices.Files.Resolve(folder) : "";
        var paths = ordered
            .Select(entry => root.Length == 0 ? entry.Path : Path.GetRelativePath(root, entry.Path))
            .ToList();

        Store(step, "files", Value.FromList(paths.Select(Value.FromText)), depth);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ListedFiles, folder, paths.Count);
    }

    /// <summary>
    /// The earliest a listing reaches back to. Nothing said is every file, which is what a step
    /// that only wants what is in a folder expects.
    /// </summary>
    private DateTimeOffset? ChangedSince(ExecutableStep step)
    {
        var count = Args(step).Number("withinCount");
        if (count <= 0)
        {
            return null;
        }

        var back = Args(step).Text("withinUnit").Trim().ToLowerInvariant() switch
        {
            "minutes" => TimeSpan.FromMinutes(count),
            "hours" => TimeSpan.FromHours(count),
            _ => TimeSpan.FromDays(count),
        };

        return DateTimeOffset.Now - back;
    }

    /// <summary>
    /// Puts a folder listing in the order asked for, falling back to the name so that two files
    /// agreeing on the key still come out the same way every run.
    /// </summary>
    private static IEnumerable<FileEntry> InOrder(IEnumerable<FileEntry> files, string key,
        bool descending)
    {
        var ordered = key.ToLowerInvariant() switch
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
