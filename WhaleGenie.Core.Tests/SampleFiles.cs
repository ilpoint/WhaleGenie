using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The files a macro author actually has in front of them, run through the actions as they stand.
/// Every other check in this project feeds the engine something the engine's own author wrote,
/// which is exactly the shape the reader was built for; a spreadsheet exported from another
/// program, a list separated by semicolons, a piece of JSON with the shape an API hands back and an
/// old .xls are the shapes nobody chose. They live in <c>testfile</c> at the top of the repository,
/// and the checks built on this harness are skipped on a machine that does not have that folder.
/// </summary>
internal static class SampleFiles
{
    /// <summary>The folder with the sample files in it.</summary>
    public static string Folder => Path.Combine(Repository(), "testfile");

    /// <summary>A step of a macro, written the way the editor writes one.</summary>
    public static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };

    /// <summary>One parameter of a step.</summary>
    public static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>
    /// Runs steps over the sample files. The bytes handed to the engine are the ones on disk — a
    /// text file is decoded the way the reader would decode it — so what the engine works on is
    /// what a macro would work on; only the devices that are not a file are stand-ins.
    /// </summary>
    public static async Task<SampleRun> RunAsync(params ExecutableStep[] steps)
    {
        var devices = new FakeDeviceLayer();
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            var name = Path.GetFileName(file);
            if (Path.GetExtension(name) is ".xlsx" or ".xls")
            {
                devices.Blobs[name] = File.ReadAllBytes(file);
            }
            else
            {
                // Decoded with the reader's own rules rather than with a second guess written here:
                // a file whose encoding the reader gets wrong has to fail in the check.
                devices.Files[name] = TextEncoding.Read(File.ReadAllBytes(file), string.Empty);
            }
        }

        var store = new VariableStore();
        var result = await new MacroRunner(store, new SilentRunHost(), devices).RunAsync(steps);
        return new SampleRun(result, store, devices);
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    public static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}

/// <summary>What one run of steps over the sample files left behind.</summary>
/// <param name="Result">How the run ended, and what it said when it did not end well.</param>
/// <param name="Store">The variables the steps set.</param>
/// <param name="Devices">
/// The stand-in devices, so a check can look at what a step wrote to a file.
/// </param>
internal sealed record SampleRun(RunResult Result, VariableStore Store, FakeDeviceLayer Devices);

/// <summary>
/// What the variables of a run hold, read the way a macro reads them.
/// </summary>
internal static class SampleVariables
{
    /// <summary>The value of one variable.</summary>
    public static Value Value(this SampleRun run, string name) => run.Store.Local.Values[name];

    /// <summary>A variable holding a list of lists, as a table read out of a file gives back.</summary>
    public static IReadOnlyList<IReadOnlyList<Value>> Rows(this SampleRun run, string name)
        => [.. run.Value(name).Items.Select(row => (IReadOnlyList<Value>)row.Items)];

    /// <summary>A variable holding a list, as the names of a header row give back.</summary>
    public static IReadOnlyList<string> Texts(this SampleRun run, string name)
        => [.. run.Value(name).Items.Select(cell => cell.AsText())];
}

/// <summary>
/// A check that needs the sample files, which are somebody's real files rather than something the
/// project ships: a machine without them reports the check as skipped instead of failing.
/// </summary>
public sealed class RealSampleFactAttribute : FactAttribute
{
    public RealSampleFactAttribute(params string[] files)
    {
        foreach (var file in files)
        {
            if (!File.Exists(Path.Combine(SampleFiles.Folder, file)))
            {
                Skip = $"This machine has no {file} to read.";
                return;
            }
        }
    }
}
