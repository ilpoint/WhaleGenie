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


namespace WhaleGenie.Core.Devices.Platform;

// Compiling C# in memory, the references it needs, and the failure it reports.
public sealed partial class ScriptDevice : IScriptDevice
{
    /// <summary>
    /// Compiles one script into a file a process can load. A script with nothing to run is told
    /// apart from one that does not compile: the first has no entry point, and the second is what
    /// the compiler complained about, in the compiler's own words and on the line they are about.
    /// </summary>
    private (string? Built, ScriptResult? Failure) Compile(string source, string folder,
        CancellationToken token)
    {
        var trees = new[]
        {
            // The encoding is written down with the source: without it the compiler refuses to
            // write down which line anything is on, and a mistake a script makes while it runs
            // would come back as a frame with no line in it.
            CSharpSyntaxTree.ParseText(CSharpServices.Source,
                new CSharpParseOptions(LanguageVersion.Latest), path: "injected.cs",
                encoding: NoMark, cancellationToken: token),
            CSharpSyntaxTree.ParseText(File.ReadAllText(source),
                new CSharpParseOptions(LanguageVersion.Latest), path: source,
                encoding: NoMark, cancellationToken: token),
        };

        var compilation = CSharpCompilation.Create("script", trees, References(),
            new CSharpCompilationOptions(OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Release));

        // A program with no Main is a program the compiler complains about; that complaint is the
        // step's answer here rather than a compilation error, because it is the script's shape
        // rather than a mistake in it.
        var complaining = compilation.GetDiagnostics(token)
            .Where(complaint => complaint.Severity == DiagnosticSeverity.Error
                && complaint.Id != NoEntryPointId)
            .ToList();

        if (complaining.Count > 0)
        {
            return (null, Failed(RunKeys.ScriptCompileFailed, Said(complaining, folder)));
        }

        if (compilation.GetEntryPoint(token) is null)
        {
            return (null, Failed(RunKeys.NoEntryPoint, string.Empty));
        }

        var built = Path.Combine(folder, "script.dll");
        using (var image = File.Create(built))
        using (var symbols = File.Create(Path.Combine(folder, "script.pdb")))
        {
            // The lines are written down with the symbols, so a mistake a script makes while it
            // runs is reported against the line it was written on, the way a compiled program is.
            var wrote = compilation.Emit(image, symbols,
                options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
                cancellationToken: token);

            if (!wrote.Success)
            {
                return (null, Failed(RunKeys.ScriptCompileFailed, Said(
                    [.. wrote.Diagnostics.Where(complaint =>
                        complaint.Severity == DiagnosticSeverity.Error)], folder)));
            }
        }

        return (built, null);
    }

    /// <summary>
    /// What the compiler said, one complaint to a line, in the shape a compiler says it: the file
    /// and the place in it, then the error's own name and words. The copy of the script is in a
    /// temporary folder of this program's making, which is nobody's business, so it is left out of
    /// the path.
    /// </summary>
    private static string Said(IReadOnlyList<Diagnostic> complaining, string folder)
    {
        const int MostSaid = 10;
        var lines = complaining.Take(MostSaid)
            .Select(complaint => complaint.ToString()
                .Replace(folder + Path.DirectorySeparatorChar, string.Empty, StringComparison.Ordinal))
            .ToList();

        if (complaining.Count > MostSaid)
        {
            lines.Add($"(and {complaining.Count - MostSaid} more)");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
