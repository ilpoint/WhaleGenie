using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

// Values a run works with: shared numbers and lists, calculations, encodings and text fields.
public partial class MacroRunnerTests
{
    [Fact]
    public async Task A_shared_variable_must_exist_before_a_macro_can_write_to_it()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.setVariable", Param("name", "shared"), Param("scope", "global"), Param("value", "1"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.GlobalMustExist", result.Key);

        store.Global.SetNumber("shared", 0);
        var (ok, _) = await RunAsync(
            [Step("control.setVariable", Param("name", "shared"), Param("scope", "global"), Param("value", "7"))],
            variables: store);

        Assert.True(ok.Succeeded);
        Assert.Equal(7, store.Global.Values["shared"].AsNumber());
    }

    /// <summary>
    /// Adding to a shared number is one action, so a macro does not have to read the value and
    /// write it back in two steps with a window in between for another run to fit into.
    /// </summary>
    [Fact]
    public async Task Adding_to_a_shared_number_keeps_the_new_value()
    {
        var store = Store();
        store.Global.SetNumber("shared", 10);

        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "2.5"))],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(12.5, store.Global.Values["shared"].AsNumber());
    }

    [Fact]
    public async Task Adding_to_a_name_the_project_does_not_declare_is_refused()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "1"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.GlobalMustExist, result.Key);
    }

    [Fact]
    public async Task Adding_to_a_shared_text_value_says_it_is_not_a_number()
    {
        var store = Store();
        store.Global.SetText("shared", "ten");

        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "1"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.NotANumber, result.Key);
    }

    /// <summary>
    /// Two runs adding at the same time both have their additions counted. Reading the number in
    /// one step and writing the sum in the next would let one of them overwrite the other.
    /// </summary>
    [Fact]
    public async Task Two_runs_adding_to_one_shared_number_lose_nothing()
    {
        var shared = new VariableBag();
        shared.SetNumber("shared", 0);
        var steps = Enumerable.Range(0, 200)
            .Select(_ => Step("control.incrementGlobal", Param("name", "shared"),
                Param("amount", "1")))
            .ToArray();

        var results = await Task.WhenAll(
            new MacroRunner(new VariableStore(shared)).RunAsync(steps),
            new MacroRunner(new VariableStore(shared)).RunAsync(steps));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Key));
        Assert.Equal(400, shared.Values["shared"].AsNumber());
    }

    /// <summary>
    /// 计数加完直接把新值交出来，后续条件或步骤不必再读一次全局——再读一次看到的可能已经是别的运行
    /// 改过的数。
    /// </summary>
    [Fact]
    public async Task Adding_to_a_shared_number_hands_over_the_new_count()
    {
        var store = Store();
        store.Global.SetNumber("shared", 10);

        var (result, _) = await RunAsync(
            [Step("control.incrementGlobal", Param("name", "shared"), Param("amount", "5"),
                Returns("value=now"))],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal(15, N(store, "now"));
        Assert.Equal(15, store.Global.Values["shared"].AsNumber());
    }

    /// <summary>
    /// 两条运行同时往同一个全局列表里追加，谁都不丢：一次追加是"读现在的列表、加一项、写回去"，
    /// 这三下在同一把锁里做完，另一条运行不会在中间插进来，也不会把对方的列表盖掉。
    /// </summary>
    [Fact]
    public async Task Two_runs_appending_to_one_shared_list_lose_nothing()
    {
        var shared = new VariableBag();
        shared.SetList("log", []);
        var steps = Enumerable.Range(0, 200)
            .Select(index => Step("control.listAdd", Param("name", "log"),
                Param("value", index.ToString())))
            .ToArray();

        var results = await Task.WhenAll(
            new MacroRunner(new VariableStore(shared)).RunAsync(steps),
            new MacroRunner(new VariableStore(shared)).RunAsync(steps));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Key));
        Assert.Equal(400, shared.Values["log"].Items.Count);
    }

    /// <summary>The same for taking items out: two runs removing from one list still remove 400.</summary>
    [Fact]
    public async Task Two_runs_removing_from_one_shared_list_lose_nothing()
    {
        var shared = new VariableBag();
        shared.SetList("log", Enumerable.Range(0, 400).Select(index => Value.FromNumber(index)));
        var steps = Enumerable.Range(0, 200)
            .Select(_ => Step("control.listRemoveAt", Param("name", "log"),
                Param("listIndex", "0")))
            .ToArray();

        var results = await Task.WhenAll(
            new MacroRunner(new VariableStore(shared)).RunAsync(steps),
            new MacroRunner(new VariableStore(shared)).RunAsync(steps));

        Assert.All(results, result => Assert.True(result.Succeeded, result.Key));
        Assert.Empty(shared.Values["log"].Items);
    }

    /// <summary>改一个没人定义的列表仍报原来的 Run.NotAList，不新建一个。</summary>
    [Fact]
    public async Task Changing_a_list_nobody_defined_still_says_it_is_not_a_list()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("control.listAdd", Param("name", "nosuch"), Param("value", "a"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.NotAList, result.Key);
    }

    /// <summary>
    /// 改不动的那一次什么都不写：越界是指这一项不存在，不是把列表改坏一半再报错。
    /// </summary>
    [Fact]
    public async Task A_list_change_that_fails_leaves_the_list_as_it_was()
    {
        var store = Store();
        store.Local.SetList("names", [Value.FromText("a")]);

        var (result, _) = await RunAsync(
            [Step("control.listSet", Param("name", "names"), Param("listIndex", "9"),
                Param("value", "b"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.IndexOutOfRange, result.Key);
        Assert.Equal(["a"], store.Local.Values["names"].Items.Select(item => item.AsText()));
    }

    [Fact]
    public async Task A_calculation_works_out_its_expression_and_stores_the_answer()
    {
        var store = Store(("count", 4));
        var (result, _) = await RunAsync(
            [Step("control.setVariable",
                Param("name", "total"), Param("scope", "local"),
                Fields.Computed("value", "count + 3"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(7, N(store, "total"));
    }

    [Fact]
    public async Task A_calculation_reads_the_arithmetic_written_in_the_expression()
    {
        var store = Store(("n", 10), ("m", 3));
        var (result, _) = await RunAsync(
            [
                Step("control.setVariable", Param("name", "minus"), Fields.Computed("value", "n - m")),
                Step("control.setVariable", Param("name", "times"), Fields.Computed("value", "n * m")),
                Step("control.setVariable", Param("name", "over"), Fields.Computed("value", "n / 4")),
                Step("control.setVariable", Param("name", "rest"), Fields.Computed("value", "n % m")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal(7, N(store, "minus"));
        Assert.Equal(30, N(store, "times"));
        Assert.Equal(2.5, N(store, "over"));
        Assert.Equal(1, N(store, "rest"));
    }

    [Fact]
    public async Task A_calculation_can_put_two_pieces_of_text_together()
    {
        var store = Store();
        store.Local.SetText("name", "Ada");
        var (result, _) = await RunAsync(
            [Step("control.setVariable",
                Param("name", "greeting"), Param("value", "\"Hello \" + $name"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("Hello Ada", store.Local.Values["greeting"].AsText());
    }

    [Fact]
    public async Task A_calculation_may_call_functions_from_its_expression()
    {
        var store = Store(("count", 3));
        store.Local.SetText("name", "Ada");
        var (result, _) = await RunAsync(
            [Step("control.setVariable",
                Param("name", "line"), Param("value", "concat(upper($name), $count)"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("ADA3", store.Local.Values["line"].AsText());
    }

    [Fact]
    public async Task A_calculation_that_divides_by_zero_fails_the_step()
    {
        var store = Store(("zero", 0));
        var (result, _) = await RunAsync(
            [Step("control.setVariable",
                Param("name", "x"), Fields.Computed("value", "zero / 0"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);
        Assert.False(store.TryGet("x", out _));
    }

    [Fact]
    public async Task Text_goes_through_Base64_and_comes_back()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [
                Step("data.base64Encode", Param("dataText", "你好, Ada"),
                    Returns("encoded")),
                Step("data.base64Decode", Fields.Reference("dataText", "encoded"),
                    Returns("decoded=back")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("你好, Ada", store.Local.Values["back"].AsText());

        // Base64 of UTF-8 bytes, which is what every service on the other end expects.
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("你好, Ada")),
            store.Local.Values["encoded"].AsText());
    }

    [Fact]
    public async Task Text_that_is_not_Base64_fails_the_step()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [Step("data.base64Decode", Param("dataText", "not base64!!"), Returns("decoded=x"))],
            variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadBase64", result.Key);
        Assert.Equal(string.Empty, store.Local.Values["x"].AsText());
    }

    [Fact]
    public async Task A_hash_is_worked_out_by_the_named_algorithm()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [
                Step("data.hash", Param("hashAlgorithm", "sha256"), Param("dataText", "abc"),
                    Returns("digest")),
                Step("data.hash", Param("hashAlgorithm", "md5"), Param("dataText", "abc"),
                    Returns("digest=md5")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            store.Local.Values["digest"].AsText());
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", store.Local.Values["md5"].AsText());
    }

    [Fact]
    public async Task Lists_can_be_created_and_changed()
    {
        var store = Store();
        var (result, _) = await RunAsync(
            [
            Step("control.setVariable", Param("name", "names"), Param("scope", "local"),
                Param("value", "list(\"a\")")),
                Step("control.listAdd", Param("name", "names"), Param("value", "b")),
                Step("control.listInsert", Param("name", "names"), Param("listIndex", "0"), Param("value", "z")),
                Step("control.listRemoveAt", Param("name", "names"), Param("listIndex", "-1")),
            ],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("z, a", store.Local.Values["names"].AsText());
    }

    [Fact]
    public async Task One_position_of_a_list_can_be_changed_without_touching_the_others()
    {
        var store = Store();
        store.Local.Set("names", Value.FromList(
            new[] { Value.FromText("a"), Value.FromText("b"), Value.FromText("c") }));

        var (result, _) = await RunAsync(
            [
                Step("control.listSet", Param("name", "names"), Param("listIndex", "1"),
                    Param("value", "B")),
                Step("control.listSet", Param("name", "names"), Param("listIndex", "-1"),
                    Param("value", "C")),
            ],
            variables: store);

        Assert.True(result.Succeeded, result.Key);
        Assert.Equal("a, B, C", store.Local.Values["names"].AsText());
    }

    [Fact]
    public async Task Changing_a_position_the_list_has_not_got_stops_the_step()
    {
        // Quietly doing nothing would leave the file the macro is about to write unchanged, and
        // nothing in the log would say why.
        var store = Store();
        store.Local.Set("names", Value.FromList(new[] { Value.FromText("a") }));

        var (result, _) = await RunAsync(
            [Step("control.listSet", Param("name", "names"), Param("listIndex", "4"),
                Param("value", "z"))],
            variables: store);

        Assert.Equal("Run.IndexOutOfRange", result.Key);
    }

    [Fact]
    public async Task A_list_change_through_a_variable_keeps_the_values_in_order()
    {
        var store = Store();
        store.Local.Set("names", Value.FromList(new[] { Value.FromText("a"), Value.FromText("b") }));
        store.Local.SetText("item", "c");

        var (result, _) = await RunAsync(
            [Step("control.listAdd", Param("name", "names"), Fields.Reference("value", "item"))],
            variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("a, b, c", store.Local.Values["names"].AsText());
    }

    /// <summary>
    /// Emptying a list is not the same as dropping the variable: a macro that clears a list and
    /// then adds to it again would otherwise be adding to nothing.
    /// </summary>
    [Fact]
    public async Task Clearing_a_list_empties_it_and_leaves_the_variable_in_place()
    {
        var store = Store();
        store.Local.Set("names", Value.FromList(new[] { Value.FromText("a"), Value.FromText("b") }));

        var (result, _) = await RunAsync(
            [Step("control.listClear", Param("name", "names"))], variables: store);

        Assert.True(result.Succeeded);
        Assert.True(store.Local.Values["names"].IsList);
        Assert.Empty(store.Local.Values["names"].Items);
        Assert.Equal(string.Empty, store.Local.Values["names"].AsText());
    }

    [Fact]
    public async Task A_value_written_as_an_expression_has_to_work()
    {
        var store = Store();
        var (result, _) = await RunAsync([Set("a", "$missing + 1")], variables: store);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal("Run.BadExpression", result.Key);
    }

    [Fact]
    public async Task Text_shaped_like_a_selector_or_a_path_stays_text()
    {
        var store = Store();
        var (result, _) = await RunAsync(
        [
            Set("selector", "[data-testid=\"row-2\"]"),
            Set("nth", "div > p:nth-of-type(2)"),
            Set("path", @"C:\Program Files (x86)\a.log"),
            Set("bracketed", "Meeting (2) notes"),
        ], variables: store);

        Assert.True(result.Succeeded);
        Assert.Equal("[data-testid=\"row-2\"]", store.Local.Values["selector"].AsText());
        Assert.Equal("div > p:nth-of-type(2)", store.Local.Values["nth"].AsText());
        Assert.Equal(@"C:\Program Files (x86)\a.log", store.Local.Values["path"].AsText());
        Assert.Equal("Meeting (2) notes", store.Local.Values["bracketed"].AsText());
    }

    [Fact]
    public async Task A_log_step_writes_what_it_is_told()
    {
        var store = Store(("who", 0));
        store.Local.SetText("who", "Ada");

        var (_, host) = await RunAsync(
            [Step("control.log", Param("message", "hello $who"), Param("level", "warn"))],
            variables: store);

        var line = host.Entries.Last(entry => entry.Key == "Log.Message");
        Assert.Equal(LogLevel.Warn, line.Level);
        Assert.Equal("hello Ada", line.Arguments[0]);
    }

    [Fact]
    public async Task A_text_field_works_out_a_braced_expression()
    {
        var store = Store(("n", 3));

        var (_, host) = await RunAsync(
            [Step("control.log", Param("message", "n is ${$n + 1}"), Param("level", "info"))],
            variables: store);

        var line = host.Entries.Last(entry => entry.Key == "Log.Message");
        Assert.Equal("n is 4", line.Arguments[0]);
    }

    [Fact]
    public async Task A_broken_braced_expression_fails_the_step()
    {
        var (result, _) = await RunAsync(
            [Step("control.log", Fields.Computed("message", "1 +"), Param("level", "info"))]);

        Assert.Equal(RunStatus.Failed, result.Status);
        Assert.Equal(RunKeys.BadExpression, result.Key);
    }
}
