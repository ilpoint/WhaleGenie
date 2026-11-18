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
            Names(Expression.Evaluate("where($rows, \"city\", \"equals\", \"London\")", store)));
        Assert.Equal(["Ada", "Alan"],
            Names(Expression.Evaluate("where($rows, \"name\", \"startsWith\", \"a\")", store)));
        Assert.Equal(["Grace"],
            Names(Expression.Evaluate("where($rows, \"name\", \"endsWith\", \"ce\")", store)));
        Assert.Equal(["Alan"],
            Names(Expression.Evaluate("where($rows, \"name\", \"contains\", \"la\")", store)));
        Assert.Equal(["Ada", "Alan"],
            Names(Expression.Evaluate("where($rows, \"city\", \"regex\", \"^lon\")", store)));
    }

    [Fact]
    public void Where_can_care_about_case_and_spaces()
    {
        var store = Table(
            ("ADA", Value.FromNumber(1), " London "),
            ("ada", Value.FromNumber(2), "London"));

        Assert.Equal(["ADA", "ada"],
            Names(Expression.Evaluate(
                "where($rows, \"name\", \"equals\", \"ada\")", store)));
        Assert.Equal(["ada"],
            Names(Expression.Evaluate(
                "where($rows, \"name\", \"equals\", \"ada\", true)", store)));
        Assert.Equal(["ada"],
            Names(Expression.Evaluate(
                "where($rows, \"name\", \"equals\", \"ada\", true, false)", store)));
        Assert.Equal(["ADA", "ada"],
            Names(Expression.Evaluate(
                "where($rows, \"city\", \"equals\", \"London\")", store)));
        Assert.Equal(["ada"],
            Names(Expression.Evaluate(
                "where($rows, \"city\", \"equals\", \"London\", false, false)", store)));
    }

    [Fact]
    public void Where_compares_numbers_by_order_and_equality()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromNumber(85), "New York"),
            ("Alan", Value.FromNumber(41), "London"));

        Assert.Equal(["Grace"],
            Names(Expression.Evaluate("where($rows, \"age\", \">\", 50)", store)));
        Assert.Equal(["Grace", "Alan"],
            Names(Expression.Evaluate("where($rows, \"age\", \">=\", 40)", store)));
        Assert.Equal(["Ada"],
            Names(Expression.Evaluate("where($rows, \"age\", \"<\", 40)", store)));
        Assert.Equal(["Ada", "Alan"],
            Names(Expression.Evaluate("where($rows, \"age\", \"<=\", 41)", store)));
        Assert.Equal(["Ada"],
            Names(Expression.Evaluate("where($rows, \"age\", \"=\", 36)", store)));
        Assert.Equal(["Grace", "Alan"],
            Names(Expression.Evaluate("where($rows, \"age\", \"!=\", 36)", store)));
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
            Names(Expression.Evaluate("where($rows, \"age\", \">\", \"40\")", store)));
    }

    [Fact]
    public void Where_leaves_out_a_row_that_cannot_answer_and_says_so()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromText("eighty-five"), "New York"),
            ("Alan", Value.FromNumber(41), "London"));

        Expression.BeginDiagnostics();
        var kept = Expression.Evaluate("where($rows, \"age\", \">\", 40)", store);
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
        var kept = Expression.Evaluate("where($rows, \"nope\", \"contains\", \"x\")", store);
        var note = Assert.Single(Expression.TakeDiagnostics());

        Assert.Empty(kept.AsList());
        Assert.Equal(LogKeys.ListFieldMissing, note.Key);

        var missing = Assert.Throws<ExpressionException>(
            () => Expression.Evaluate("column($rows, \"nope\")", store));
        Assert.Contains("name, age, city", missing.Detail, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Where_on_an_empty_list_is_an_empty_list()
        => Assert.Empty(Expression.Evaluate(
            "where(list(), \"age\", \">\", 1)").AsList());

    [Fact]
    public void Column_takes_one_field_from_every_record()
    {
        var store = Table(
            ("Ada", Value.FromNumber(36), "London"),
            ("Grace", Value.FromNumber(85), "New York"));

        var ages = Expression.Evaluate("column($rows, \"age\")", store).AsList();

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
        var ages = Expression.Evaluate("column($rows, \"age\")", store).AsList();
        var note = Assert.Single(Expression.TakeDiagnostics());

        Assert.Equal([36d], ages.Select(age => age.AsNumber()));
        Assert.Equal(LogKeys.ListFieldMissing, note.Key);
        Assert.Equal(2, note.Arguments[0]);
    }

    [Fact]
    public void A_comparison_word_the_language_does_not_know_is_refused()
    {
        var failure = Assert.Throws<ExpressionException>(() => Expression.Evaluate(
            "where(list(), \"age\", \"olderThan\", 1)"));

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
                new ExecutableParameter { Name = "name", Text = "kept" },
                new ExecutableParameter { Name = "scope", Text = "local" },
                new ExecutableParameter
                {
                    Name = "value",
                    Text = "where($rows, \"age\", \">\", 40)",
                },
            ],
        };

        var result = await new MacroRunner(store, host).RunAsync([step]);

        Assert.True(result.Succeeded, result.Key);
        Assert.Contains(host.Entries, entry => entry.Key == LogKeys.ListFieldNotNumber);
    }
}
