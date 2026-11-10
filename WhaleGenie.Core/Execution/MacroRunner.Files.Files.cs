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
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// One file at a time: whether it is there, deleting, copying and moving it.
public sealed partial class MacroRunner
{
    private void FileExists(ExecutableStep step, int depth)
    {
        var found = _devices.Files.Exists(PathOf(step));
        Store(step, "exists", Value.FromBool(found), depth);
    }

    private void DeleteFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var recycle = Flag(step, "toRecycleBin", false);
        _devices.Files.Delete(path, recycle);
        Log(LogLevel.Info, depth, step.Type,
            recycle ? LogKeys.RecycledFile : LogKeys.DeletedFile, path);
    }

    private void CopyFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var (to, go) = Landing(step, PathOf(step, "to"));
        if (!go)
        {
            Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
            Log(LogLevel.Info, depth, step.Type, LogKeys.FileLeftAlone, to);
            return;
        }

        _devices.Files.Copy(from, to);
        Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
        Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedFile, from, to);
    }

    private void MoveFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var (to, go) = Landing(step, PathOf(step, "to"));
        if (!go)
        {
            Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
            Log(LogLevel.Info, depth, step.Type, LogKeys.FileLeftAlone, to);
            return;
        }

        _devices.Files.Move(from, to);
        Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
        Log(LogLevel.Info, depth, step.Type, LogKeys.MovedFile, from, to);
    }

    /// <summary>
    /// Where a copy or a move is to land, and what to do about a file already sitting there: put
    /// this one over it, leave the one that is there alone, or give the new one a number of its own
    /// beside it. Whether the name is taken is a question only the machine can answer.
    /// </summary>
    private (string To, bool Go) Landing(ExecutableStep step, string to)
    {
        var choice = ReadText(step, "ifExists").Trim().ToLowerInvariant();
        if (!_devices.Files.Exists(to))
        {
            return (to, true);
        }

        return choice switch
        {
            "skip" => (to, false),
            "unique" => (Beside(to), true),
            _ => (to, true),
        };
    }

    /// <summary>
    /// A free name next to a taken one — report (2).csv, then report (3).csv — which is the way
    /// Windows itself gives a second file a name of its own.
    /// </summary>
    private string Beside(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var number = 2; number < 10_000; number++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({number}){extension}");
            if (!_devices.Files.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new StepFailure(RunKeys.NoFreeName, path);
    }
}
