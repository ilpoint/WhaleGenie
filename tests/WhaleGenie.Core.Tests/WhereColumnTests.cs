using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Reading a table into records is only half the job: the next thing a macro wants is the few
/// rows that matter and one column out of them. These checks keep those two expressions on the
/// same comparison words the rest of the program uses, and keep a row that cannot answer from
/// quietly becoming a zero.
/// </summary>
public class WhereColumnTests
{
    private static VariableStore Table(params (string Name, Value Age, string City)[] rows)
    {
        var store = new VariableStore();
        store.Local.Set("rows", Value.FromList(rows.Select(row => Value.FromRecord(
        [
            ("name", Value.FromText(row.Name)),
            ("age", row.Age),
            ("city", Value.FromText(row.City)),
        ]))));
        return store;
    }

    private static IReadOnlyList<string> Names(Value value)
        => [.. value.AsList().Select(row => row.Field("name")!.AsText())];

    [Fact]
    public void Where_keeps_rows_whose_text_field_matches()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromNumber(85), "New York"),
            ("Alan", Value.FromNumber(41), "London"));

        Assert.Equal(["Ada", "Alan"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("city"), Formula.Text("equals"), Formula.Text("London")), store)));
        Assert.Equal(["Ada", "Alan"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("name"), Formula.Text("startsWith"), Formula.Text("a")), store)));
        Assert.Equal(["Grace"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("name"), Formula.Text("endsWith"), Formula.Text("ce")), store)));
        Assert.Equal(["Alan"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("name"), Formula.Text("contains"), Formula.Text("la")), store)));
        Assert.Equal(["Ada", "Alan"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("city"), Formula.Text("regex"), Formula.Text("^lon")), store)));
    }

    [Fact]
    public void Where_can_care_about_case_and_spaces()
    {
        var store = Table(
            ("ADA", Value.FromNumber(1), " London "),
            ("ada", Value.FromNumber(2), "London"));

        Assert.Equal(["ADA", "ada"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("name"), Formula.Text("equals"), Formula.Text("ada")), store)));
        Assert.Equal(["ada"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("name"), Formula.Text("equals"), Formula.Text("ada"), Formula.Constant("true")), store)));
        Assert.Equal(["ada"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("name"), Formula.Text("equals"), Formula.Text("ada"), Formula.Constant("true"), Formula.Constant("false")), store)));
        Assert.Equal(["ADA", "ada"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("city"), Formula.Text("equals"), Formula.Text("London")), store)));
        Assert.Equal(["ada"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("city"), Formula.Text("equals"), Formula.Text("London"), Formula.Constant("false"), Formula.Constant("false")), store)));
    }

    [Fact]
    public void Where_compares_numbers_by_order_and_equality()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromNumber(85), "New York"),
            ("Alan", Value.FromNumber(41), "London"));

        Assert.Equal(["Grace"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text(">"), Formula.Number("50", 50d)), store)));
        Assert.Equal(["Grace", "Alan"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text(">="), Formula.Number("40", 40d)), store)));
        Assert.Equal(["Ada"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text("<"), Formula.Number("40", 40d)), store)));
        Assert.Equal(["Ada", "Alan"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text("<="), Formula.Number("41", 41d)), store)));
        Assert.Equal(["Ada"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text("="), Formula.Number("36", 36d)), store)));
        Assert.Equal(["Grace", "Alan"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text("!="), Formula.Number("36", 36d)), store)));
    }

    /// <summary>
    /// A CSV cell is text even when it says 42. A numeric comparison has to read that text, or a
    /// table read from a file would have every row skipped by the very filter it is meant for.
    /// </summary>
    [Fact]
    public void Where_reads_a_number_written_in_a_csv_cell_as_a_number()
    {
        var store = Table(
            ("Ada", Value.FromText("36"), "London"),
            ("Grace", Value.FromText("85"), "New York"),
            ("Alan", Value.FromText("eighty-five"), "London"));

        Assert.Equal(["Grace"],
            Names(Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text(">"), Formula.Text("40")), store)));
    }

    [Fact]
    public void Where_leaves_out_a_row_that_cannot_answer_and_says_so()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromText("eighty-five"), "New York"),
            ("Alan", Value.FromNumber(41), "London"));

        Expression.BeginDiagnostics();
        var kept = Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text(">"), Formula.Number("40", 40d)), store);
        var notes = Expression.TakeDiagnostics();

        Assert.Equal(["Alan"], Names(kept));
        var note = Assert.Single(notes);
        Assert.Equal(LogKeys.ListFieldNotNumber, note.Key);
        Assert.Equal(2, note.Arguments[0]);
    }

    [Fact]
    public void Where_skips_a_missing_field_and_lists_the_fields_it_does_not_find()
    {
        var store = Table(("Ada", Value.FromNumber(36), "London"));

        Expression.BeginDiagnostics();
        var kept = Expression.Evaluate(Formula.Call("where", Formula.Name("rows"), Formula.Text("nope"), Formula.Text("contains"), Formula.Text("x")), store);
        var note = Assert.Single(Expression.TakeDiagnostics());

        Assert.Empty(kept.AsList());
        Assert.Equal(LogKeys.ListFieldMissing, note.Key);

        var missing = Assert.Throws<ExpressionException>(
            () => Expression.Evaluate(Formula.Call("column", Formula.Name("rows"), Formula.Text("nope")), store));
        Assert.Contains("name, age, city", missing.Detail, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Where_on_an_empty_list_is_an_empty_list()
        => Assert.Empty(Expression.Evaluate(Formula.Call("where", Formula.Call("list"), Formula.Text("age"), Formula.Text(">"), Formula.Number("1", 1d))).AsList());

    [Fact]
    public void Column_takes_one_field_from_every_record()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromNumber(85), "New York"));

        var ages = Expression.Evaluate(Formula.Call("column", Formula.Name("rows"), Formula.Text("age")), store).AsList();

        Assert.Equal([36d, 85d], ages.Select(age => age.AsNumber()));
    }

    [Fact]
    public void Column_skips_a_row_without_the_field_and_says_so()
    {
        var store = new VariableStore();
        store.Local.Set("rows", Value.FromList(
        [
            Value.FromRecord([("name", Value.FromText("Ada")), ("age", Value.FromNumber(36))]),
            Value.FromRecord([("name", Value.FromText("Grace"))]),
        ]));

        Expression.BeginDiagnostics();
        var ages = Expression.Evaluate(Formula.Call("column", Formula.Name("rows"), Formula.Text("age")), store).AsList();
        var note = Assert.Single(Expression.TakeDiagnostics());

        Assert.Equal([36d], ages.Select(age => age.AsNumber()));
        Assert.Equal(LogKeys.ListFieldMissing, note.Key);
        Assert.Equal(2, note.Arguments[0]);
    }

    [Fact]
    public void A_comparison_word_the_language_does_not_know_is_refused()
    {
        var failure = Assert.Throws<ExpressionException>(() => Expression.Evaluate(Formula.Call("where", Formula.Call("list"), Formula.Text("age"), Formula.Text("olderThan"), Formula.Number("1", 1d))));

        Assert.Contains("olderThan", failure.Detail, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// A row left out by where is worth a line in the run log: the macro carries on with the rows
    /// that did answer, but the reason one is missing is not left for the user to guess.
    /// </summary>
    [Fact]
    public async Task A_row_left_out_by_where_is_written_to_the_run_log()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromText("eighty-five"), "New York"));
        var host = new SilentRunHost();
        var step = new ExecutableStep
        {
            Type = "control.setVariable",
            Parameters =
            [
                Fields.Text("name", "kept"),
                Fields.Text("scope", "local"),
                Fields.Computed("value", Formula.Call("where", Formula.Name("rows"), Formula.Text("age"), Formula.Text(">"), Formula.Number(40))),
            ],
        };

        var result = await new MacroRunner(store, host).RunAsync([step]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains(host.Entries, entry => entry.Key == LogKeys.ListFieldNotNumber);
    }
}
