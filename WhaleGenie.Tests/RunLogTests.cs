using System;
using WhaleGenie.Core.Execution;
using WhaleGenie.Execution;

namespace WhaleGenie.Tests;

/// <summary>The run history MCP reads: the same lines the window showed, kept after it is gone.</summary>
public class RunLogTests
{
    [Fact]
    public void A_run_keeps_its_lines_outcomes_and_failure_path()
    {
        RunLogStore.Clear();
        var capture = RunLogStore.Begin("测试", "debug");
        capture.Add(new LogEntry(DateTimeOffset.Now, LogLevel.Info, 0, string.Empty,
            "Log.Started", []), "开始");
        capture.Add(new LogEntry(DateTimeOffset.Now, LogLevel.Info, 0, string.Empty,
            LogKeys.FailureLog, ["C:\\logs\\failure.log"]), "失败日志");
        capture.Ran(new StepRun
        {
            StepId = "k3f9",
            StepType = "vision.findImage",
            Outcome = "ok",
            Results =
            [
                new StepResult("found", "命中", "true"),
                new StepResult("match", string.Empty, "{\"x\":10,\"y\":20}"),
            ],
        });

        capture.Finish(new RunResult(RunStatus.Failed, "Run.Test", "detail", 3), "失败");

        var run = Assert.Single(RunLogStore.Recent());
        Assert.Equal("测试", run.Macro);
        Assert.Equal("debug", run.Source);
        Assert.Equal(2, run.Lines.Count);
        Assert.Equal("C:\\logs\\failure.log", run.FailureLogPath);
        Assert.Equal("Run.Test", run.Key);

        // A step's answer is kept even when the macro never kept it: what it worked out is what a
        // reader compares against what they expected.
        var outcome = Assert.Single(run.Outcomes);
        Assert.Equal("k3f9", outcome.StepId);
        Assert.Equal("vision.findImage", outcome.StepType);
        Assert.Equal("ok", outcome.Outcome);
        Assert.Equal(2, outcome.Results.Count);
        Assert.True(outcome.Results[0].Kept);
        Assert.False(outcome.Results[1].Kept);
        Assert.Equal("match", outcome.Results[1].Key);
    }

    /// <summary>An answer too long to be one line comes back shortened rather than whole.</summary>
    [Fact]
    public void A_very_long_answer_is_shortened()
    {
        RunLogStore.Clear();
        var capture = RunLogStore.Begin("长值", "debug");
        capture.Ran(new StepRun
        {
            StepId = "aaaa",
            StepType = "file.readText",
            Outcome = "ok",
            Results = [new StepResult("text", "内容", new string('x', 1000))],
        });
        capture.Finish(new RunResult(RunStatus.Completed, string.Empty, string.Empty, 1), "完成");

        var outcome = Assert.Single(Assert.Single(RunLogStore.Recent()).Outcomes);
        Assert.Equal(301, outcome.Results[0].Value.Length);
        Assert.EndsWith("…", outcome.Results[0].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void A_triggered_host_can_feed_the_same_capture()
    {
        RunLogStore.Clear();
        var capture = RunLogStore.Begin("触发", "trigger");
        var host = new AskRunHost(null, null, capture.Add);

        host.Log(new LogEntry(DateTimeOffset.Now, LogLevel.Warn, 1, "step", LogKeys.Finished, []));

        var run = Assert.Single(RunLogStore.Recent());
        Assert.Single(run.Lines);
        Assert.False(string.IsNullOrWhiteSpace(run.Lines[0].Text));
    }

    [Fact]
    public void The_store_keeps_only_the_last_twenty_runs()
    {
        RunLogStore.Clear();
        for (var index = 0; index < 21; index++)
        {
            var capture = RunLogStore.Begin(index.ToString(), "debug");
            capture.Finish(null, "done");
        }

        Assert.Equal(20, RunLogStore.Recent(20).Count);
        Assert.DoesNotContain(RunLogStore.Recent(20), run => run.Macro == "0");
    }
}
