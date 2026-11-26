using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The file family on its own: the fake disk answers what it holds and records what it was told, so
/// reading, shaping and writing a file can be checked without touching this computer.
/// </summary>
public class FileActionTests
{
    [Fact]
    public async Task Reading_a_text_file_hands_over_the_text_and_its_lines()
    {
        var context = new FakeRunContext();
        context.Machine.Files.Store["note.txt"] = "first\r\nsecond\r\n";

        await Run(context, "file.readText", Step("file.readText",
            Fields.Text("path", "note.txt"),
            SampleFiles.Returns("text", "lines")));

        Assert.Equal("first\r\nsecond\r\n", context.Written["text"].AsText());
        Assert.Equal(["first", "second"],
            context.Written["lines"].Items.Select(line => line.AsText()));
    }

    [Fact]
    public async Task Writing_a_text_file_leaves_the_text_where_the_step_named()
    {
        var context = new FakeRunContext();

        await Run(context, "file.writeText", Step("file.writeText",
            Fields.Text("path", "out.txt"),
            Fields.Text("fileContent", "hello")));

        Assert.Equal("hello", context.Machine.Files.Store["out.txt"]);
    }

    [Fact]
    public async Task A_csv_is_handed_over_as_one_record_per_row_named_by_its_header()
    {
        var context = new FakeRunContext();
        context.Machine.Files.Store["people.csv"] = "name,age\nAnn,30\nBob,41\n";

        await Run(context, "file.readCsv", Step("file.readCsv",
            Fields.Text("path", "people.csv"),
            Fields.Text("separator", ","),
            SampleFiles.Returns("rows")));

        var rows = context.Written["rows"].Items;
        Assert.Equal(2, rows.Count);
        Assert.Equal("Ann", Field(rows[0], FieldNames.Name).AsText());
        Assert.Equal("30", Field(rows[0], "age").AsText());
        Assert.Equal("Bob", Field(rows[1], FieldNames.Name).AsText());
    }

    [Fact]
    public async Task Saving_variables_writes_the_ones_the_step_named()
    {
        var context = new FakeRunContext();
        ((IRunContext)context).Variables.Set("who", Value.FromText("Ann"));
        ((IRunContext)context).Variables.Set("total", Value.FromNumber(3));

        await Run(context, "file.saveVariables", Step("file.saveVariables",
            Fields.Text("path", "saved.json"),
            Fields.Text("names", "who")));

        var saved = context.Machine.Files.Store["saved.json"];
        Assert.Contains("\"who\"", saved);
        Assert.DoesNotContain("\"total\"", saved);
    }

    private static Value Field(Value record, string name)
        => record.Fields.First(field => field.Name == name).Value;

    private static Task<Signal> Run(FakeRunContext context, string type, ExecutableStep step)
        => Handler(type)(context, step, 0, CancellationToken.None);

    private static RunActionHandler Handler(string type)
        => FileActions.Map.TryGetValue(type, out var handler) ? handler : FileCsvActions.Map[type];

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Parameters = parameters };
}
