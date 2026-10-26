using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WhaleGenie.Mcp;

/// <summary>How one check ended. Missing is not a failure: it says the surface this run offers has
/// no such thing, which is what the report is here to show.</summary>
internal enum McpCheckResult
{
    Pass,
    Fail,
    Missing,
    Skip,
}

/// <summary>One thing the self-check asked the server and what came back.</summary>
internal sealed record McpCheck(string Name, McpCheckResult Result, string Detail);

/// <summary>One tool call, with the cost it carried. Cost is what makes "usable" a number.</summary>
internal sealed record McpCall(
    string Tool, int RequestBytes, int ResponseBytes, long ElapsedMs, bool Failed);

/// <summary>
/// What one self-check found. It is written as markdown so the same run can be pasted into a
/// document or diffed between two builds; the numbers it carries are the ones a change is judged
/// by before and after.
/// </summary>
internal sealed class McpSelfCheckReport
{
    public string Endpoint { get; init; } = string.Empty;

    public DateTimeOffset At { get; init; } = DateTimeOffset.Now;

    public List<McpCheck> Checks { get; } = [];

    public List<McpCall> Calls { get; } = [];

    public Dictionary<string, double> Metrics { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, string> Facts { get; } = new(StringComparer.Ordinal);

    public int Failed => Checks.Count(check => check.Result == McpCheckResult.Fail);

    public int Missing => Checks.Count(check => check.Result == McpCheckResult.Missing);

    public bool Passed => Failed == 0;

    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.Append("## WhaleGenie MCP self-check — ").Append(Endpoint).Append(" — ")
            .Append(At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append('\n');
        text.Append('\n');
        text.Append("Result: ").Append(Passed ? "pass" : "fail")
            .Append(", ").Append(Checks.Count(check => check.Result == McpCheckResult.Pass))
            .Append(" pass, ").Append(Failed).Append(" fail, ")
            .Append(Missing).Append(" missing, ")
            .Append(Checks.Count(check => check.Result == McpCheckResult.Skip)).Append(" skip\n");
        text.Append('\n');

        Table(text, ["check", "result", "detail"],
            Checks.Select(check => new[] { check.Name, Word(check.Result), check.Detail }));

        if (Metrics.Count > 0)
        {
            Table(text, ["metric", "value"],
                Metrics.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new[]
                    {
                        pair.Key,
                        pair.Value.ToString("0.##", CultureInfo.InvariantCulture),
                    }));
        }

        if (Facts.Count > 0)
        {
            Table(text, ["fact", "value"],
                Facts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new[] { pair.Key, pair.Value }));
        }

        if (Calls.Count > 0)
        {
            Table(text, ["#", "tool", "request", "response", "ms", "error"],
                Calls.Select((call, index) => new[]
                {
                    (index + 1).ToString(CultureInfo.InvariantCulture),
                    call.Tool,
                    call.RequestBytes.ToString(CultureInfo.InvariantCulture),
                    call.ResponseBytes.ToString(CultureInfo.InvariantCulture),
                    call.ElapsedMs.ToString(CultureInfo.InvariantCulture),
                    call.Failed ? "yes" : string.Empty,
                }));
        }

        return text.ToString();
    }

    private static string Word(McpCheckResult result) => result switch
    {
        McpCheckResult.Pass => "pass",
        McpCheckResult.Fail => "fail",
        McpCheckResult.Missing => "missing",
        _ => "skip",
    };

    private static void Table(StringBuilder text, string[] headings, IEnumerable<string[]> rows)
    {
        text.Append("| ").Append(string.Join(" | ", headings.Select(Cell))).Append(" |\n");
        text.Append('|');
        foreach (var _ in headings)
        {
            text.Append(" --- |");
        }

        text.Append('\n');
        foreach (var row in rows)
        {
            text.Append("| ").Append(string.Join(" | ", row.Select(Cell))).Append(" |\n");
        }

        text.Append('\n');
    }

    // A detail can carry quotes, pipes or a newline from an error message; left alone any of them
    // would break the table it is written into.
    private static string Cell(string value)
        => value.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
}
