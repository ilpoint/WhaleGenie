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
        capture.Finish(new RunResult(RunStatus.Failed, "Run.Test", "detail", 3), "失败",
            [new RunLogOutcome("step.k3f9.outcome", "第 1 步", "failed")]);

        var run = Assert.Single(RunLogStore.Recent());
        Assert.Equal("测试", run.Macro);
        Assert.Equal("debug", run.Source);
        Assert.Equal(2, run.Lines.Count);
        Assert.Equal("C:\\logs\\failure.log", run.FailureLogPath);
        Assert.Equal("Run.Test", run.Key);
        Assert.Single(run.Outcomes);
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
