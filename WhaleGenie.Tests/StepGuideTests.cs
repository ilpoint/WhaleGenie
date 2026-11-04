using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The guide lines are drawn a row at a time, so the way they go wrong is not a wrong shape but a
/// column with a hole in it: one row draws the line below itself, the next starts one row too low,
/// and the column has a gap nobody can explain by looking at either row alone.
///
/// What a column promises is simple: from the box of the node that owns it, down to the last row of
/// that node's subtree. That is what this checks, column by column, rather than the shape of any one
/// row.
/// </summary>
public class StepGuideTests
{
    [Fact]
    public void A_column_runs_from_its_node_down_to_the_last_row_of_its_subtree()
    {
        Ui.Run(() =>
        {
            var macro = new MacroItem { Name = "probe" };
            var window = new MacroEditorWindow(macro, [macro]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var editor = (MacroEditorViewModel)window.DataContext!;
            var loop = Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "3" },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps =
                    [
                        Step("input.mouseMove"),
                        Step("control.if",
                            new StepParameter { Name = "condition", Kind = ActionParameterKind.Text, Value = "$a" },
                            new StepParameter
                            {
                                Name = "then",
                                Kind = ActionParameterKind.Steps,
                                Steps = [Step("control.delay")],
                            },
                            new StepParameter
                            {
                                Name = "else",
                                Kind = ActionParameterKind.Steps,
                                Steps = [Step("control.log")],
                            }),
                    ],
                });
            editor.AddStep(loop);
            Dispatcher.UIThread.RunJobs();

            var tree = window.FindControl<TreeView>("StepList")!;
            var boxes = window.GetVisualDescendants().OfType<Panel>()
                .Where(panel => panel.Name == "RowHeader" && panel.DataContext is StepRow)
                .Select(panel => (Row: (StepRow)panel.DataContext!,
                    Top: panel.TranslatePoint(new Point(0, 0), tree)!.Value.Y,
                    X: panel.TranslatePoint(new Point(0, 0), tree)!.Value.X,
                    panel.Bounds.Height))
                .OrderBy(entry => entry.Row.RowIndex)
                .ToList();

            var where = boxes.ToDictionary(entry => entry.Row, entry => entry);

            // Every line a row draws, as a column and a span in the tree's own coordinates.
            var drawn = new Dictionary<int, List<(double Top, double Bottom)>>();
            foreach (var entry in boxes)
            {
                var middle = entry.Top + (entry.Height / 2);
                foreach (var line in entry.Row.Guides)
                {
                    var column = (int)Math.Round(entry.X + line.Left);
                    var span = line.Tail
                        ? (middle, entry.Top + entry.Height)
                        : (entry.Top, line.Full ? entry.Top + entry.Height : middle);

                    if (!drawn.TryGetValue(column, out var spans))
                    {
                        drawn[column] = spans = [];
                    }

                    spans.Add(span);
                }
            }

            var holes = new List<string>();
            foreach (var row in editor.Rows)
            {
                if (row.Children.Count == 0)
                {
                    continue;
                }

                // The column this row owns: under its own chevron, running from its middle down to
                // where its last child turns into its own row. It does not run to the bottom of the
                // whole subtree — below the last child the line belongs to that child's column.
                var column = (int)Math.Round(where[row].X - StepRow.ChevronCenter);
                var last = row.Children[^1];
                if (!where.ContainsKey(last))
                {
                    continue;
                }

                var from = where[row].Top + (where[row].Height / 2);
                var to = where[last].Top + (where[last].Height / 2);

                if (!drawn.TryGetValue(column, out var spans))
                {
                    holes.Add($"{row.Step.Type} d{row.Depth}: column {column} empty");
                    continue;
                }

                var covered = from;
                foreach (var span in spans.OrderBy(span => span.Top))
                {
                    if (span.Top >= to)
                    {
                        break;
                    }

                    if (span.Bottom <= covered)
                    {
                        continue;
                    }

                    if (span.Top - covered > 6)
                    {
                        holes.Add($"{row.Step.Type} d{row.Depth}@{where[row].Top:0} -> {last.Step.Type} d{last.Depth}@{where[last].Top:0}: column {column} {covered:0}->{span.Top:0}");
                    }

                    covered = Math.Max(covered, span.Bottom);
                }

                if (to - covered > 6)
                {
                    holes.Add($"{row.Step.Type} d{row.Depth}: column {column} ends at {covered:0}, last child {to:0}");
                }
            }

            Assert.True(holes.Count == 0, "columns with a hole: " + string.Join(" | ", holes));
            Ui.CloseAndAnswer(window);
        });
    }

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };
}
