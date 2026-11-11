using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// What a macro declares about itself: its returns, record shapes, and starting values.
public partial class MacroRunnerTests
{
    [Fact]
    public async Task A_row_that_names_a_column_the_list_does_not_have_stops_the_step()
    {
        var returns = Fields.Rows("returns", Cells.Of(("key", "path"), ("name", "part"), ("bogus", "x")));

        var (result, _) = await RunAsync(
            [Step("file.path", Param("operation", "name"), Param("path", "a.txt"), returns)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadRowColumn, result.Key);
        Assert.Equal("returns.bogus", result.Detail);
    }

    [Fact]
    public async Task A_row_that_leaves_out_a_required_column_stops_the_step()
    {
        var returns = Fields.Rows("returns", Cells.Of(("key", "path")));

        var (result, _) = await RunAsync(
            [Step("file.path", Param("operation", "name"), Param("path", "a.txt"), returns)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadRowColumn, result.Key);
        Assert.Equal("returns.name", result.Detail);
    }

    [Fact]
    public async Task A_return_key_the_action_does_not_declare_stops_the_step_before_it_runs()
    {
        var returns = Fields.Rows("returns", Cells.Of(
            (FieldNames.Key, "missing"),
            (FieldNames.Name, "取回")));

        var (result, _) = await RunAsync(
        [
            Step("file.path", Param("operation", "name"), Param("path", "a.txt"), returns),
        ]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadResult, result.Key);
        Assert.Equal("missing", result.Detail);
    }

    [Fact]
    public async Task Two_results_cannot_be_written_to_the_same_name()
    {
        var returns = Fields.Rows("returns", ReturnsRow("text", "取回"), ReturnsRow("lines", "取回"));

        var (result, _) = await RunAsync([Step("file.readText", Param("path", "a.txt"),
            returns)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadResult, result.Key);
        Assert.Equal("取回", result.Detail);
    }

    [Fact]
    public async Task A_return_name_the_macro_language_cannot_read_stops_the_step()
    {
        var returns = Fields.Rows("returns", ReturnsRow("text", "two words"));

        var (result, _) = await RunAsync([Step("file.readText", Param("path", "a.txt"),
            returns)]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadResult, result.Key);
        Assert.Equal("two words", result.Detail);
    }

    [Fact]
    public void A_declared_record_shape_is_checked_field_by_field()
    {
        var program = ActionSemanticsCatalog.Find("process.start")!.Return("program")!;
        var exact = Value.FromRecord(
        [
            (FieldNames.Id, Value.FromNumber(12)),
            (FieldNames.Process, Value.FromText("notepad")),
            (FieldNames.Title, Value.FromText("Untitled")),
            (FieldNames.Handle, Value.FromNumber(0)),
            (FieldNames.How, Value.FromText(WindowHow.ProcessEquals)),
        ]);

        Assert.True(MacroRunner.ReturnFits(program, exact));
        Assert.False(MacroRunner.ReturnFits(program, Value.FromRecord(
        [
            (FieldNames.Id, Value.FromNumber(12)),
            (FieldNames.Process, Value.FromText("notepad")),
            (FieldNames.Title, Value.FromText("Untitled")),
        ])));
        Assert.False(MacroRunner.ReturnFits(program, Value.FromRecord(
        [
            (FieldNames.Id, Value.FromText("十二")),
            (FieldNames.Process, Value.FromText("notepad")),
            (FieldNames.Title, Value.FromText("Untitled")),
            (FieldNames.Handle, Value.FromNumber(0)),
        ])));
    }

    [Fact]
    public void Every_item_of_a_declared_record_list_has_the_same_shape()
    {
        var every = ActionSemanticsCatalog.Find("vision.findImage")!.Return("every")!;
        var first = RecordOf(every.Fields);
        var second = RecordOf(every.Fields);

        Assert.True(MacroRunner.ReturnFits(every, Value.FromList([first, second])));
        Assert.False(MacroRunner.ReturnFits(every,
            Value.FromList([first, Value.FromText("not a record")])));
    }

    [Fact]
    public async Task A_declared_local_starts_every_run_at_the_value_it_declares()
    {
        var signature = new MacroSignature([], [],
            [new MacroLocal("total", "5", ActionReturnType.Number)]);
        var store = Store();

        var (first, _) = await RunAsync([Set("total", "9")], variables: store,
            startSignature: signature);
        Assert.True(first.Succeeded, first.Key);
        Assert.Equal(9, N(store, "total"));

        // The second run does not inherit what the first one left: an initial value is written at
        // the start of every run, not only when the name is missing.
        var (second, _) = await RunAsync([], variables: store, startSignature: signature);
        Assert.True(second.Succeeded, second.Key);
        Assert.Equal(5, N(store, "total"));
    }

    [Fact]
    public async Task A_declared_local_reads_a_parameter_and_the_ones_declared_before_it()
    {
        var signature = new MacroSignature(
            [new MacroArgument("base", "", true, ActionReturnType.Number)], [],
            [
                new MacroLocal("doubled", "$base * 2", ActionReturnType.Number),
                new MacroLocal("plus", "$doubled + 1", ActionReturnType.Number),
            ]);
        var store = Store();

        var (result, _) = await RunAsync([], variables: store, startSignature: signature,
            startArguments: [new MacroStartArgument("base", "4", false)]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(8, N(store, "doubled"));
        Assert.Equal(9, N(store, "plus"));
    }

    [Fact]
    public async Task A_declared_local_left_empty_starts_at_the_empty_value_of_its_kind()
    {
        var signature = new MacroSignature([], [],
            [
                new MacroLocal("count", "", ActionReturnType.Number),
                new MacroLocal("items", "", ActionReturnType.List),
            ]);
        var store = Store();

        var (result, _) = await RunAsync([], variables: store, startSignature: signature);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(0, N(store, "count"));
        Assert.True(store.Local.Values["items"].IsList);
    }

    [Fact]
    public async Task A_called_macro_starts_at_its_own_declaration_not_the_callers()
    {
        var store = Store();
        store.Local.Set("base", Value.FromNumber(100));
        var library = new Library(("内部", new MacroSignature([], [new MacroReturn("结果")],
            [
                new MacroLocal("base", "10"),
                new MacroLocal("derived", "$base + 1"),
            ]), [Set("结果", "$derived")]));

        var (result, _) = await RunAsync([Call("内部", Returns("结果=结果"))],
            variables: store, macros: library);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(11, N(store, "结果"));
    }

    [Fact]
    public async Task A_declaration_that_repeats_a_name_stops_the_run_before_it_starts()
    {
        var signature = new MacroSignature([], [],
            [new MacroLocal("n", "1"), new MacroLocal("n", "2")]);

        var (result, _) = await RunAsync([Set("seen", "1")], startSignature: signature);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadDeclaration, result.Key);
    }

    [Fact]
    public async Task A_starting_value_that_cannot_be_the_kind_it_declares_stops_the_run()
    {
        var signature = new MacroSignature([], [],
            [new MacroLocal("count", "many", ActionReturnType.Number)]);

        var (result, _) = await RunAsync([Set("seen", "1")], startSignature: signature);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadInitial, result.Key);
    }

    /// <summary>A call to another macro of the stand-in project.</summary>
    private static ExecutableStep Call(string macro, params ExecutableParameter[] more)
        => Step("control.runMacro", [Param("macro", macro), .. more]);

    private static Dictionary<string, string> ReturnsRow(string key, string name) => new(
        StringComparer.OrdinalIgnoreCase)
    {
        [FieldNames.Key] = key,
        [FieldNames.Name] = name,
    };

    private static Value RecordOf(IReadOnlyList<ActionReturnField> fields)
        => Value.FromRecord(fields.Select(field => (field.Name, SampleValue(field.Type))));

    private static Value SampleValue(ActionReturnType type) => type switch
    {
        ActionReturnType.Number => Value.FromNumber(1),
        ActionReturnType.Flag => Value.FromBool(true),
        ActionReturnType.List => Value.FromList([]),
        ActionReturnType.Record => Value.FromRecord([]),
        _ => Value.FromText(string.Empty),
    };

    /// <summary>
    /// A macro that declares what a call has to fill in and may ask for. Every argument is
    /// required, which is what a macro that reads a value it was handed tends to want.
    /// </summary>
    private static (string Name, MacroSignature Signature, ExecutableStep[] Steps) Takes(
        string name, string[] arguments, string[] results, params ExecutableStep[] steps)
        => (name, new MacroSignature(
            [.. arguments.Select(argument => new MacroArgument(argument, string.Empty, true))],
            [.. results.Select(name => new MacroReturn(name))], []), steps);

    /// <summary>The same for a macro that asks for nothing, which is a macro called for what it does.</summary>
    private static (string Name, MacroSignature Signature, ExecutableStep[] Steps) Plain(
        string name, params ExecutableStep[] steps) => (name, MacroSignature.Empty, steps);

    /// <summary>A stand-in for the macros of one project, so a call can be tested on its own.</summary>
    private sealed class Library : IMacroLibrary
    {
        private readonly Dictionary<string, (IReadOnlyList<ExecutableStep> Steps,
            MacroSignature Signature)> _byName = new(StringComparer.OrdinalIgnoreCase);

        public Library(params (string Name, MacroSignature Signature, ExecutableStep[] Steps)[] macros)
        {
            foreach (var (name, signature, steps) in macros)
            {
                _byName[name] = (steps, signature);
            }
        }

        public IReadOnlyList<string> Names => [.. _byName.Keys];

        public IReadOnlyList<ExecutableStep>? Steps(string name)
            => _byName.TryGetValue(name, out var macro) ? macro.Steps : null;

        public MacroSignature? Signature(string name)
            => _byName.TryGetValue(name, out var macro) ? macro.Signature : null;
    }

    private sealed class WatchingHost : IRunHost
    {
        public System.Collections.Generic.List<ExecutableStep> Seen { get; } = [];

        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
        {
            Seen.Add(step);
            return Task.FromResult(true);
        }

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
            => Task.FromResult(StepErrorChoice.Stop);

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);

        /// <summary>Nothing these runs reach cannot be undone.</summary>
        public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
            => Task.FromResult(true);
    }

    /// <summary>Answers the questions a failure asks, then refuses to answer more.</summary>
    private sealed class AskingHost(params StepErrorChoice[] answers) : IRunHost
    {
        private int _index;

        public int Asked => _index;

        /// <summary>What the runner said each time it asked the user.</summary>
        public System.Collections.Generic.List<(string Step, string Reason, string Detail, string Comment)> Questions { get; } = [];

        public void Log(LogEntry entry)
        {
        }

        public string Text(LogEntry entry) => entry.Key;

        public Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
            => Task.FromResult(true);

        public Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
            CancellationToken token)
        {
            Questions.Add((step, reason, detail, comment));
            var answer = _index < answers.Length ? answers[_index] : StepErrorChoice.Stop;
            _index++;
            return Task.FromResult(answer);
        }

        public Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
            => Task.FromResult(AskAnswer.Cancel);

        /// <summary>Nothing these runs reach cannot be undone.</summary>
        public Task<bool> ConfirmIrreversible(ExecutableStep step, CancellationToken token)
            => Task.FromResult(true);
    }
}
