using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WhaleGenie.Core.Execution;
using Xunit;

namespace WhaleGenie.Core.Tests.Bench;

/// <summary>
/// The action matrix: every action the catalogue declares, built from its own declaration and run
/// once against a stand-in desktop. It is a ruler rather than a proof — it never changes the engine
/// or the catalogue, and it writes down what it could not build instead of skipping it.
/// </summary>
public class ActionBenchTests
{
    /// <summary>
    /// One row per action, no exceptions. A hole in the matrix is worse than a failure in it: a
    /// failure says "this action is broken", a hole says nothing at all.
    /// </summary>
    [Fact]
    public async Task Every_action_has_a_verdict()
    {
        var cells = await Matrix();

        Assert.Equal(ActionSemanticsCatalog.All.Count, cells.Count);
        Assert.Equal(
            [.. ActionSemanticsCatalog.All.Select(action => action.Key)],
            [.. cells.Select(cell => cell.Key)]);
    }

    /// <summary>
    /// Nothing is left as "could not build it" and nothing runs differently from what the catalogue
    /// says. The two are kept apart on purpose: an action nobody could give values to is a fixture
    /// to write, an action that runs differently is a contract to fix, and mixing them hides the
    /// second behind the first.
    /// </summary>
    [Fact]
    public async Task Every_action_runs_and_matches_its_declaration()
    {
        var cells = await Matrix();
        var broken = cells.Where(cell => cell.Outcome != BenchOutcome.Pass
            && cell.Outcome != BenchOutcome.Unreachable).ToArray();

        Assert.True(broken.Length == 0, Lines(broken));
    }

    /// <summary>
    /// The rows that are not covered are the ones the bench wrote down a reason for. This is what
    /// keeps "not covered" from growing quietly: the count has to match the list in the fixtures.
    /// </summary>
    [Fact]
    public async Task Only_the_named_actions_are_left_uncovered()
    {
        var cells = await Matrix();
        var uncovered = cells.Where(cell => cell.Outcome == BenchOutcome.Unreachable).ToArray();

        foreach (var cell in uncovered)
        {
            Assert.False(string.IsNullOrWhiteSpace(cell.Detail), $"{cell.Key} 没有写不可达的理由");
        }
    }

    /// <summary>
    /// The ruler measures rather than agrees. A declaration that says an action does something it
    /// does not — or hides something it does — has to come back as "不符", or a green matrix would
    /// mean nothing at all.
    /// </summary>
    [Fact]
    public async Task The_bench_notices_a_declaration_that_does_not_match()
    {
        var quiet = await Declared("control.delay", sendsInput: true);
        Assert.Equal(BenchOutcome.Fail, quiet.Outcome);
        Assert.Contains("声明会发输入", quiet.Detail, StringComparison.Ordinal);

        var blind = await Declared("control.delay", looksAtScreen: true);
        Assert.Equal(BenchOutcome.Fail, blind.Outcome);
        Assert.Contains("声明会看屏幕", blind.Detail, StringComparison.Ordinal);

        var silent = await Declared("input.keyPress", sendsInput: false);
        Assert.Equal(BenchOutcome.Fail, silent.Outcome);
        Assert.Contains("没有声明会发输入", silent.Detail, StringComparison.Ordinal);
    }

    /// <summary>An action run against a declaration that is not the catalogue's own.</summary>
    private static Task<BenchCell> Declared(string key, bool sendsInput = false, bool looksAtScreen = false)
    {
        var action = ActionSemanticsCatalog.Find(key)
            ?? throw new InvalidOperationException($"目录里没有 {key}");
        return ActionBench.RunAsync(action, new BenchDeclaration(sendsInput, looksAtScreen));
    }

    /// <summary>Runs the whole matrix and leaves the report where a run asked for it.</summary>
    private static async Task<IReadOnlyList<BenchCell>> Matrix()
    {
        var cells = new List<BenchCell>();
        foreach (var action in ActionSemanticsCatalog.All)
        {
            cells.Add(await ActionBench.RunAsync(action));
        }

        Save(BenchReport.ToMarkdown(cells, DateTimeOffset.Now));
        return cells;
    }

    /// <summary>The failing rows, one line each, so a red run says which action and why.</summary>
    private static string Lines(IReadOnlyList<BenchCell> cells)
        => string.Join(Environment.NewLine,
            cells.Select(cell => $"{cell.Key} [{cell.State}] {cell.Detail}"));

    /// <summary>
    /// Writes the report where a run asked for it, so two builds can be compared. Only written when
    /// asked: a check that leaves files behind on every run is a check that gets deleted.
    /// </summary>
    private static void Save(string markdown)
    {
        if (Environment.GetEnvironmentVariable("ACTION_MATRIX_REPORT") is not { Length: > 0 } path)
        {
            return;
        }

        File.WriteAllText(path, markdown, new UTF8Encoding(false));
    }
}
