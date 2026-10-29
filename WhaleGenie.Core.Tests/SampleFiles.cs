using System;
using System.Collections.Generic;
using System.Globalization;
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

    /// <summary>
    /// A step of a macro, written the way the editor writes one. Window rules are one record, so a
    /// check may keep saying a title and the one choice beside it; this is where that becomes the
    /// shape a package holds.
    /// </summary>
    public static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
    {
        var hasWindow = parameters.Any(parameter => parameter.Name == "window");
        var how = hasWindow
            ? parameters.FirstOrDefault(parameter => parameter.Name == "how")?.Text
            : null;
        var written = new List<ExecutableParameter>(parameters.Length);
        foreach (var parameter in parameters)
        {
            if (hasWindow && parameter.Name == "how")
            {
                continue;
            }

            if (parameter.Name != "window")
            {
                written.Add(parameter);
                continue;
            }

            var text = parameter.Text ?? string.Empty;
            if (text.TrimStart().StartsWith('$'))
            {
                written.Add(parameter);
                continue;
            }

            var rule = WindowRule.TryParse(text, out var parsed)
                ? parsed
                : new WindowRule(Title: text);
            if (how is { Length: > 0 })
            {
                rule = how == WindowHow.Handle
                    ? rule with { How = how, Handle = long.TryParse(text, out var handle) ? handle : 0 }
                    : how is WindowHow.Process or WindowHow.ProcessEquals
                    ? rule with { How = how, Process = text }
                    : how is WindowHow.Class or WindowHow.ClassEquals
                        ? rule with { How = how, ClassName = text }
                        : rule with { How = how, Title = text };
            }

            written.Add(Param("window", rule.ToJson().ToJsonString()));
        }

        return new ExecutableStep { Type = type, Parameters = [.. written] };
    }

    /// <summary>
    /// The same step with a clock of its own. A step that waits for something says how long it may
    /// wait in its own settings rather than in a field of the action, so a check that lets a wait
    /// run out — or that keeps one from running forever — says so here. What happens when the
    /// clock runs out is the step's failure rule, which a check that wants the run to carry on
    /// names here too.
    /// </summary>
    public static ExecutableStep Within(int milliseconds, ExecutableStep step,
        StepErrorAction onError = StepErrorAction.Stop, string id = "")
        => new()
        {
            Type = step.Type,
            // A step's name is a setting of the step too, and the outcome of a step that has one
            // is written under it, so a check that reads an outcome names the step here.
            Id = id.Length > 0 ? id : step.Id,
            Parameters = step.Parameters,
            Meta = new StepMeta { TimeoutMs = milliseconds, OnError = onError },
        };

    /// <summary>One parameter of a step.</summary>
    public static ExecutableParameter Param(string name, string text = "")
        => new() { Name = name, Text = text };

    /// <summary>
    /// Where a step looks, written the way the dialog writes it: one row per place, each row giving
    /// its rectangle by column name.
    /// </summary>
    public static ExecutableParameter Region(
        params (int X, int Y, int Width, int Height)[] rectangles)
        => new()
        {
            Name = "region",
            Rows = [.. rectangles.Select(rectangle =>
                (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    ["x"] = rectangle.X.ToString(CultureInfo.InvariantCulture),
                    ["y"] = rectangle.Y.ToString(CultureInfo.InvariantCulture),
                    ["width"] = rectangle.Width.ToString(CultureInfo.InvariantCulture),
                    ["height"] = rectangle.Height.ToString(CultureInfo.InvariantCulture),
                })],
        };

    /// <summary>A place to look that names a variable instead of giving four numbers.</summary>
    public static ExecutableParameter RegionFrom(string named)
        => new()
        {
            Name = "region",
            Rows =
            [
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["text"] = named,
                },
            ],
        };

    /// <summary>
    /// The one part of the screen a step reads or copies, written the way the dialog writes it: four
    /// ordinary fields of the step rather than a row of its own, because a rectangle a step works on
    /// directly is the four numbers it says.
    /// </summary>
    public static ExecutableParameter[] Coords(int x, int y, int width, int height)
        =>
        [
            Param("x", x.ToString(CultureInfo.InvariantCulture)),
            Param("y", y.ToString(CultureInfo.InvariantCulture)),
            Param("width", width.ToString(CultureInfo.InvariantCulture)),
            Param("height", height.ToString(CultureInfo.InvariantCulture)),
        ];

    /// <summary>Copies one part of the screen: the four numbers it covers, then the rest of its settings.</summary>
    public static ExecutableStep Capture(int x, int y, int width, int height,
        params ExecutableParameter[] rest)
        => Step("vision.capture", [.. Coords(x, y, width, height), .. rest]);

    /// <summary>Reads one part of the screen: the four numbers it covers, then the rest of its settings.</summary>
    public static ExecutableStep Recognize(int x, int y, int width, int height,
        params ExecutableParameter[] rest)
        => Step("ocr.recognize", [.. Coords(x, y, width, height), .. rest]);

    /// <summary>
    /// Copies the whole screen: a step that names no rectangle at all, which is how one is written
    /// when the four numbers are left empty.
    /// </summary>
    public static ExecutableStep CaptureWholeScreen(params ExecutableParameter[] rest)
        => Step("vision.capture", rest);

    /// <summary>Reads the whole screen: a step that names no rectangle at all.</summary>
    public static ExecutableStep RecognizeWholeScreen(params ExecutableParameter[] rest)
        => Step("ocr.recognize", rest);

    /// <summary>
    /// The results a step was told to hand over, one row per result: "text" hands it over under the
    /// name <c>text</c>, and "image=shot" hands the picture over under <c>shot</c>. A step that ticks
    /// nothing hands nothing over at all, so a macro that reads it reads a name no run wrote.
    /// </summary>
    public static ExecutableParameter Returns(params string[] results)
        => new()
        {
            Name = "returns",
            Rows = [.. results.Select(Result)],
        };

    /// <summary>
    /// The results of a step that found something: one record under <paramref name="name"/> holding
    /// every part of the hit and — with <paramref name="every"/> — the whole list of them under
    /// <c>name.every</c>. A search hands a hit over as one record — where it was, how big it was,
    /// how sure the reading was — so the parts of it are read with <c>field(变量, "x")</c> rather
    /// than as a variable each, and asking for the list is what makes the step collect every hit.
    /// </summary>
    public static ExecutableParameter Hit(string key, string name, bool every = false)
        => every ? Returns($"{key}={name}", $"every={name}.every") : Returns($"{key}={name}");

    /// <summary>
    /// The result a step hands over under a name, plus only the extra results the test reads. The
    /// action catalogue now rejects a result key the action does not declare, so a shared list of
    /// every possible part would make the test request results its action cannot hand back.
    /// </summary>
    public static ExecutableParameter Parts(string key, string name, params string[] extra) => Returns(
    [
        $"{key}={name}",
        .. extra.Select(part => $"{part}={name}.{part}"),
    ]);

    private static IReadOnlyDictionary<string, string> Result(string written)
    {
        var cut = written.IndexOf('=');
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["key"] = cut < 0 ? written : written[..cut],
            ["name"] = cut < 0 ? written : written[(cut + 1)..],
        };
    }

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
            // Only the text samples are read: the folder is somebody's own, and it holds workbooks
            // and macro packages beside the files these checks ask for — bytes that are not text
            // would be decoded into variables nothing reads.
            if (Path.GetExtension(name) is not (".csv" or ".json" or ".txt"))
            {
                continue;
            }

            // Decoded with the reader's own rules rather than with a second guess written here:
            // a file whose encoding the reader gets wrong has to fail in the check.
            devices.Files.Store[name] = TextEncoding.Read(File.ReadAllBytes(file), string.Empty);
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

    /// <summary>A variable holding a list of rows, as a table read out of a file gives back.</summary>
    public static IReadOnlyList<Value> Rows(this SampleRun run, string name)
        => [.. run.Value(name).Items];

    /// <summary>
    /// One cell of a row, read the way a macro reads it: by the name above the column, or by the
    /// letter of the column when the file has no header row.
    /// </summary>
    public static Value Cell(this Value row, string column)
        => row.Field(column) ?? throw new InvalidOperationException(
            $"row {row.AsText()} has no column named {column}");
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
