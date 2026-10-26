using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace WhaleGenie.Core.Tests.Bench;

/// <summary>
/// The matrix as a Markdown file: one row per action, a count per outcome, and the list of rows that
/// are not passing. It is written by the test run and kept in the repository, so "before" and
/// "after" are two files anyone can compare rather than two memories.
/// </summary>
internal static class BenchReport
{
    /// <summary>The whole report: what was measured, what it came to, and what is left to fix.</summary>
    public static string ToMarkdown(IReadOnlyList<BenchCell> cells, DateTimeOffset at)
    {
        var text = new StringBuilder();
        text.AppendLine("# 动作矩阵");
        text.AppendLine();
        text.AppendLine($"生成时间：{at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}；"
            + $"动作 {cells.Count} 个。");
        text.AppendLine();
        text.AppendLine("这份文件由 `WhaleGenie.Core.Tests/Bench` 生成，不是手写的。每个动作都按目录声明合成一步，");
        text.AppendLine("在替身设备层上跑一遍，然后核四件事：返回值是否写出来且形状对、副作用声明是否与设备账本一致、");
        text.AppendLine("结局是否符合预期、有没有参数根本给不出值。改前改后各留一份，数字对不上就是代码变了。");
        text.AppendLine();

        Counts(text, cells);
        Gaps(text, cells);
        All(text, cells);
        return text.ToString();
    }

    /// <summary>How the 176 came out, by outcome and by group.</summary>
    private static void Counts(StringBuilder text, IReadOnlyList<BenchCell> cells)
    {
        var total = cells.Count;
        var pass = cells.Count(cell => cell.Outcome == BenchOutcome.Pass);
        var fail = cells.Count(cell => cell.Outcome == BenchOutcome.Fail);
        var none = cells.Count(cell => cell.Outcome == BenchOutcome.NoFixture);
        var unreachable = cells.Count(cell => cell.Outcome == BenchOutcome.Unreachable);

        text.AppendLine("## 合计");
        text.AppendLine();
        text.AppendLine("| 通过 | 不符 | 缺夹具 | 不可达 | 合计 |");
        text.AppendLine("| ---: | ---: | ---: | ---: | ---: |");
        text.AppendLine($"| {pass} | {fail} | {none} | {unreachable} | {total} |");
        text.AppendLine();
        text.AppendLine("| 分组 | 动作 | 通过 | 不符 | 缺夹具 | 不可达 |");
        text.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var group in cells.GroupBy(cell => cell.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            text.AppendLine($"| {group.Key} | {group.Count()} "
                + $"| {group.Count(cell => cell.Outcome == BenchOutcome.Pass)} "
                + $"| {group.Count(cell => cell.Outcome == BenchOutcome.Fail)} "
                + $"| {group.Count(cell => cell.Outcome == BenchOutcome.NoFixture)} "
                + $"| {group.Count(cell => cell.Outcome == BenchOutcome.Unreachable)} |");
        }

        text.AppendLine();
    }

    /// <summary>Everything that is not a pass, named one by one: this is the work list.</summary>
    private static void Gaps(StringBuilder text, IReadOnlyList<BenchCell> cells)
    {
        var gaps = cells.Where(cell => cell.Outcome != BenchOutcome.Pass).ToArray();
        text.AppendLine("## 待修清单");
        text.AppendLine();
        if (gaps.Length == 0)
        {
            text.AppendLine("没有：每个动作都跑通，且声明与账本一致。");
            text.AppendLine();
            return;
        }

        text.AppendLine("| 动作 | 结论 | 说明 |");
        text.AppendLine("| --- | --- | --- |");
        foreach (var cell in gaps)
        {
            text.AppendLine($"| `{cell.Key}` | {cell.State} | {Escape(cell.Detail)} |");
        }

        text.AppendLine();
    }

    /// <summary>Every action, in the catalogue's own order, so a reader can check none is missing.</summary>
    private static void All(StringBuilder text, IReadOnlyList<BenchCell> cells)
    {
        text.AppendLine("## 逐行");
        text.AppendLine();
        text.AppendLine("| 动作 | 分组 | 结论 | 说明 |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var cell in cells)
        {
            var detail = cell.Outcome == BenchOutcome.Pass
                ? string.Join("；", cell.Notes)
                : cell.Detail;
            text.AppendLine($"| `{cell.Key}` | {cell.Category} | {cell.State} | {Escape(detail)} |");
        }
    }

    /// <summary>A cell's text with the pipes and line breaks that would break the table taken out.</summary>
    private static string Escape(string text)
        => text.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}
