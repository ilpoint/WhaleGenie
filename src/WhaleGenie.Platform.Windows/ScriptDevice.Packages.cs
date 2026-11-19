using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGet.Common;
using WhaleGenie.Core.Expressions;


namespace WhaleGenie.Platform.Windows;

// Installing python and nuget packages, and tidying what they leave behind.
public sealed partial class ScriptDevice : IScriptDevice
{

    // --------------------------------------------------------------- packages

    public ScriptResult InstallPip(PipRequest request, Action<string> onOutput, Action<string> onError,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var missing = request.Packages.Where(package => !PipInstalled(package)).ToList();
        if (missing.Count == 0)
        {
            return new ScriptResult(0, string.Empty, string.Empty, Value.FromList([]));
        }

        Directory.CreateDirectory(PythonPackages);
        var arguments = new List<string>
        {
            "-m", "pip", "install", "--disable-pip-version-check", "--upgrade",
            "--target", Quoted(PythonPackages),
        };
        arguments.AddRange(missing.Select(Quoted));

        CommandResult ran;
        try
        {
            var (program, flags) = CommandLine.Split(request.Command);
            var written = flags.Length == 0
                ? string.Join(' ', arguments)
                : $"{flags} {string.Join(' ', arguments)}";

            ran = _processes.Run(new CommandRequest(program, written,
                TimeoutMs: request.TimeoutMs <= 0 ? int.MaxValue : request.TimeoutMs,
                OnOutput: onOutput, OnError: onError,
                OutputEncoding: TextEncoding.System));
        }
        catch (DeviceActionException failure) when (failure.Key == RunKeys.ProgramFailed)
        {
            // pip is run through the interpreter named above, so a program that cannot be started
            // at all means this machine has no Python rather than a pip that refused.
            throw new DeviceActionException(RunKeys.NoPython, request.Command);
        }

        var added = missing.Where(PipInstalled).ToList();
        return new ScriptResult(ran.ExitCode, ran.StandardOutput, ran.StandardError,
            Value.FromList([.. added.Select(Value.FromText)]));
    }

    /// <summary>
    /// Reads which NuGet packages a macro names and brings the ones that are not there yet down
    /// from the official feed, dependencies and all, so that a C# script can be compiled against
    /// them. What went in is handed over as a list, and everything that went wrong — a package that
    /// is not on the feed, a feed that cannot be reached, a run that went on too long — is handed
    /// back as the step's own error message rather than thrown, because the three results of the
    /// step are read after its failure rule has let the macro carry on.
    /// </summary>
    public ScriptResult InstallNuGet(NuGetRequest request, Action<string> onOutput,
        Action<string> onError, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var store = new NuGetStore(NuGetPackages);
        using var limit = request.TimeoutMs > 0
            ? CancellationTokenSource.CreateLinkedTokenSource(token)
            : null;
        limit?.CancelAfter(request.TimeoutMs);

        try
        {
            var wanted = NuGetStore.Written(request.Packages);
            var missing = wanted.Where(package => !store.There(package)).ToList();

            // Nothing to download is still a run worth making: the list of what a C# script is
            // compiled against is written from what is on disk, which is what puts a package back
            // into that list after the folders were moved about by hand.
            var added = Task.Run(() => store.Fetch(missing, NullLogger.Instance, onOutput,
                limit?.Token ?? token)).GetAwaiter().GetResult();

            return new ScriptResult(0, string.Empty, string.Empty,
                Value.FromList([.. added.Select(Value.FromText)]));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new DeviceActionException(RunKeys.CommandTimeout, Feed);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            return new ScriptResult(1, string.Empty,
                failure is DeviceActionException known ? known.Detail : failure.Message,
                Value.FromList([]), RunKeys.NuGetFailed);
        }
    }

    /// <summary>Where NuGet packages come from, for the line a run that went on too long writes.</summary>
    private const string Feed = "nuget.org";

    /// <summary>
    /// Whether a package is already in the folder pip installs into. The folder says so by itself:
    /// pip writes a <c>.dist-info</c> folder beside the code it unpacks, named after the package and
    /// its version, and reading that costs nothing where asking pip would mean starting an
    /// interpreter and going to the index this check exists to avoid.
    /// </summary>
    private bool PipInstalled(string package)
    {
        if (!Directory.Exists(PythonPackages))
        {
            return false;
        }

        var (name, version) = Wanted(package);
        var wanted = Normalised(name);

        foreach (var folder in Directory.EnumerateDirectories(PythonPackages, "*.dist-info"))
        {
            var stem = Path.GetFileNameWithoutExtension(folder);
            var dash = stem.LastIndexOf('-');
            if (dash <= 0 || Normalised(stem[..dash]) != wanted)
            {
                continue;
            }

            if (version.Length == 0 || stem[(dash + 1)..] == version)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A package as it is written and, where it says so, the version it has to be. Only the one
    /// spelling pip itself calls exact — <c>name==version</c> — is read here; anything else is left
    /// to pip, which understands all of it, at the price of installing a package that was in fact
    /// already there.
    /// </summary>
    private static (string Name, string Version) Wanted(string package)
    {
        var written = package.Trim();
        var at = written.IndexOf("==", StringComparison.Ordinal);
        return at > 0 ? (written[..at], written[(at + 2)..]) : (written, string.Empty);
    }

    /// <summary>How pip spells a package name in a folder, without the differences that do not count.</summary>
    private static string Normalised(string name)
        => name.Trim().Replace('-', '_').Replace('.', '_').ToLowerInvariant();

    // ----------------------------------------------------------------- shared

    /// <summary>Where the copies of a script are written while it runs.</summary>
    private static string ScriptFolder() => Path.Combine(Path.GetTempPath(), "WhaleGenie");

    private static string Quoted(string text) => $"\"{text}\"";

    /// <summary>Removes the copies of a script once nothing is reading them.</summary>
    private static void Tidy(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException
            or DirectoryNotFoundException)
        {
            // A copy that cannot be removed is not worth failing a step over: it is in the system's
            // own temporary folder, which Windows clears out by itself.
        }
    }

    /// <summary>UTF-8 that leaves the byte-order mark off, which is how a script is written.</summary>
    private static readonly UTF8Encoding NoMark = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// UTF-8 with a mark, which is what AutoHotkey reads a script of its own language as: without
    /// one it reads the system code page, and a script with Chinese text in it comes out as rubbish.
    /// </summary>
    private static readonly UTF8Encoding Mark = new(encoderShouldEmitUTF8Identifier: true);
}
