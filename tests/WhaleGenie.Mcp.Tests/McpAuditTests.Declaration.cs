using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace WhaleGenie.Mcp.Tests;

/// <summary>
/// What a tool declares is what the audit log holds. Each tool says it only looks, that its calls
/// are recorded from a summary template, or that it writes the line itself — and the line that comes
/// out has to name what the call was about and how it ended, including why it did nothing.
/// </summary>
public partial class McpAuditTests
{
    [Fact]
    public async Task A_template_writes_the_line_it_declares()
    {
        CallToolResult? answer = null;
        var lines = await LinesAfter(async () =>
            answer = await Tool(nameof(BenchTools.Run)).AuditedAsync(
                () => new ValueTask<CallToolResult>(McpErrors.Ok(new { ok = true })),
                Arguments("""{"name":"慢","operations":[1,2,3],"shape":{"name":"x","steps":[]}}""")));

        Assert.True(answer!.IsError is not true);
        var line = Assert.Single(lines);
        Assert.Contains("whalegenie_bench_run", line, StringComparison.Ordinal);

        // The name is the argument itself, and a list and an object are how many of what they hold:
        // the line says what the call was about without carrying the request.
        Assert.Contains("run 慢 3 items 2 fields", line, StringComparison.Ordinal);
        Assert.EndsWith("\tok", line, StringComparison.Ordinal);
    }

    /// <summary>An argument the call left out writes nothing, so the template still reads as one line.</summary>
    [Fact]
    public void A_template_reads_as_one_sentence_when_an_argument_is_missing()
    {
        Assert.Equal("stop", McpAuditSummary.Render("stop {runId}", null));
        Assert.Equal("stop", McpAuditSummary.Render("stop {runId}", Arguments("""{"runId":""}""")));
        Assert.Equal("stop abc12", McpAuditSummary.Render("stop {runId}", Arguments("""{"runId":"abc12"}""")));
    }

    [Fact]
    public async Task A_failure_writes_the_code_it_was_refused_by()
    {
        CallToolResult? answer = null;
        var lines = await LinesAfter(async () =>
            answer = await Tool(nameof(BenchTools.Refuse)).AuditedAsync(
                () => new ValueTask<CallToolResult>(
                    McpErrors.Fail(McpErrors.NoSuchMacro, "There is no macro named \"慢\".")),
                Arguments("""{"name":"慢"}""")));

        Assert.True(answer!.IsError);
        var line = Assert.Single(lines);
        Assert.Contains("refuse 慢", line, StringComparison.Ordinal);
        Assert.EndsWith("\t" + McpErrors.NoSuchMacro, line, StringComparison.Ordinal);
    }

    /// <summary>A call that stopped on an exception is recorded before the exception goes on.</summary>
    [Fact]
    public async Task An_exception_is_recorded_before_it_is_thrown()
    {
        var lines = await LinesAfter(async () =>
            await Assert.ThrowsAsync<McpFailure>(() => Tool(nameof(BenchTools.Thrown)).AuditedAsync(
                Thrown, null)));

        var line = Assert.Single(lines);
        Assert.Contains("throw", line, StringComparison.Ordinal);
        Assert.EndsWith("\t" + McpErrors.RunBusy, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_only_tool_writes_nothing()
    {
        CallToolResult? answer = null;
        var lines = await LinesAfter(async () =>
            answer = await Tool(nameof(BenchTools.Look)).AuditedAsync(
                () => new ValueTask<CallToolResult>(McpErrors.Ok(new { total = 0 })), null));

        Assert.True(answer!.IsError is not true);
        Assert.Empty(lines);
    }

    [Fact]
    public async Task A_tool_that_writes_its_own_line_is_not_written_twice()
    {
        var lines = await LinesAfter(async () =>
            await Tool(nameof(BenchTools.Handled)).AuditedAsync(() =>
            {
                McpAudit.Note("whalegenie_bench_handled", "did the work");
                return new ValueTask<CallToolResult>(McpErrors.Ok(new { ok = true }));
            }, null));

        var line = Assert.Single(lines);
        Assert.Contains("did the work", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_method_declares_exactly_one_way()
    {
        Assert.True(Of(nameof(BenchTools.Look)).ReadOnly);

        var audited = Of(nameof(BenchTools.Run));
        Assert.False(audited.ReadOnly);
        Assert.False(audited.Handled);
        Assert.Equal("run {name} {operations} {shape}", audited.Summary);

        Assert.True(Of(nameof(BenchTools.Handled)).Handled);

        // A tool that says nothing about its audit behaviour, or says two things, is refused where
        // the server is built: a missing line is otherwise found only after the change was made.
        Assert.Throws<InvalidOperationException>(() => Of(nameof(BenchTools.Undeclared)));
        Assert.Throws<InvalidOperationException>(() => Of(nameof(BenchTools.BothWays)));
        Assert.Throws<InvalidOperationException>(() => Of(nameof(BenchTools.Unnamed)));
    }

    private static McpToolDeclaration Of(string method)
        => McpToolDeclaration.Of(Method(method));

    private static System.Reflection.MethodInfo Method(string method)
        => typeof(BenchTools).GetMethod(method)!;

    private static McpAuditedTool Tool(string method)
        => new(McpServerTool.Create(Method(method), new BenchTools(), null),
            McpToolDeclaration.Of(Method(method)));

    private static IDictionary<string, JsonElement> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.Clone());
    }

    private static ValueTask<CallToolResult> Thrown()
        => throw new McpFailure(McpErrors.RunBusy, "\"慢\" is already running.");

    /// <summary>Runs one call against a folder of its own, and answers the lines it wrote.</summary>
    private static async Task<string[]> LinesAfter(Func<Task> call)
    {
        var folder = Fresh();
        var original = McpAudit.Folder;
        McpAudit.Folder = folder;
        try
        {
            await call();
            return File.Exists(McpAudit.File) ? File.ReadAllLines(McpAudit.File) : [];
        }
        finally
        {
            McpAudit.Folder = original;
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// The four ways a tool declares its audit behaviour, the two declarations that are refused, and
    /// one template that names no argument, which is how a call with no object of its own is written.
    /// </summary>
    [McpServerToolType]
    internal sealed class BenchTools
    {
        [McpReadOnly]
        [McpServerTool(Name = "whalegenie_bench_look"), Description("Looks.")]
        public Task<CallToolResult> Look() => Task.FromResult(McpErrors.Ok(new { }));

        [McpAudited("run {name} {operations} {shape}")]
        [McpServerTool(Name = "whalegenie_bench_run"), Description("Runs.")]
        public Task<CallToolResult> Run(string name) => Task.FromResult(McpErrors.Ok(new { name }));

        [McpAudited("refuse {name}")]
        [McpServerTool(Name = "whalegenie_bench_refuse"), Description("Refuses.")]
        public Task<CallToolResult> Refuse(string name) => Task.FromResult(McpErrors.Ok(new { name }));

        [McpAudited("throw")]
        [McpServerTool(Name = "whalegenie_bench_throw"), Description("Stops.")]
        public Task<CallToolResult> Thrown() => Task.FromResult(McpErrors.Ok(new { }));

        [McpAudited(Handled = true)]
        [McpServerTool(Name = "whalegenie_bench_handled"), Description("Records itself.")]
        public Task<CallToolResult> Handled() => Task.FromResult(McpErrors.Ok(new { }));

        [McpServerTool(Name = "whalegenie_bench_undeclared"), Description("Says nothing.")]
        public Task<CallToolResult> Undeclared() => Task.FromResult(McpErrors.Ok(new { }));

        [McpReadOnly]
        [McpAudited(Handled = true)]
        [McpServerTool(Name = "whalegenie_bench_both"), Description("Says two things.")]
        public Task<CallToolResult> BothWays() => Task.FromResult(McpErrors.Ok(new { }));

        [McpAudited()]
        [McpServerTool(Name = "whalegenie_bench_unnamed"), Description("Has no template of its own.")]
        public Task<CallToolResult> Unnamed() => Task.FromResult(McpErrors.Ok(new { }));
    }
}
