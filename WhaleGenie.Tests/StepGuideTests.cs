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
                                Steps = [Step("control.note"), Step("control.note"), Step("control.note")],
                            },
                            new StepParameter
                            {
                                Name = "else",
                                Kind = ActionParameterKind.Steps,
                                Steps =
                                [
                                    Step("control.try",
                                        new StepParameter { Name = "body", Kind = ActionParameterKind.Steps },
                                        new StepParameter { Name = "catch", Kind = ActionParameterKind.Steps },
                                        new StepParameter { Name = "finally", Kind = ActionParameterKind.Steps }),
                                ],
                            }),
                        Step("input.mouseMove"),
                    ],
                });

            // A block that is followed by another row, with a row after its last child as well:
            // both columns have a reason to run on, and neither may run on past the row it was
            // reaching for.
            editor.AddStep(Step("input.mouseMove"));
            editor.AddStep(loop);
            editor.AddStep(Step("control.note"));
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
            var drawn = new Dictionary<int, List<(double Top, double Bottom, int RowIndex)>>();
            foreach (var entry in boxes)
            {
                var middle = entry.Top + (entry.Height / 2);
                foreach (var line in entry.Row.Guides)
                {
                    var column = (int)Math.Round(entry.X + line.Left);
                    var span = line.Tail
                        ? (Top: middle, Bottom: entry.Top + entry.Height)
                        : (Top: entry.Top, Bottom: line.Full ? entry.Top + entry.Height : middle);

                    if (!drawn.TryGetValue(column, out var spans))
                    {
                        drawn[column] = spans = [];
                    }

                    spans.Add((Top: span.Top, Bottom: span.Bottom, RowIndex: entry.Row.RowIndex));
                }
            }

            // A row may only draw columns above itself: the trunk for the first level, one column
            // per level it hangs under after that. A line drawn at its own box, or to the right of
            // it, belongs to a node this row has nothing to do with.
            var strays = new List<string>();
            foreach (var entry in boxes)
            {
                // The line dropping from a row's own box to its first child is the one line a row
                // draws at its own column; everything else it draws is above itself.
                foreach (var line in entry.Row.Guides)
                {
                    var column = (int)Math.Round((entry.X + line.Left - StepRow.ChevronCenter) / StepRow.IndentWidth);

                    if (line.Tail)
                    {
                        if (column != entry.Row.Depth)
                        {
                            strays.Add($"{entry.Row.Step.Type} d{entry.Row.Depth}: drop at column {column}");
                        }

                        continue;
                    }

                    var allowed = entry.Row.Depth == 0 ? 0 : entry.Row.Depth - 1;
                    if (column < 0 || column > allowed)
                    {
                        strays.Add($"{entry.Row.Step.Type} d{entry.Row.Depth}: column {column} (allowed 0..{allowed})");
                    }
                }
            }

            var holes = new List<string>();
            foreach (var row in editor.Rows)
            {
                if (row.Children.Count == 0 || row.Depth == 0)
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

                // Only the rows inside this node's own branch: the row of a sibling further down the
                // list can share the same column, and its lines say nothing about this one.
                var inside = spans
                    .Where(span => span.RowIndex >= row.RowIndex && span.RowIndex <= last.RowIndex)
                    .OrderBy(span => span.Top)
                    .ToList();

                var covered = from;
                foreach (var span in inside)
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

                // And it must not run on past that child either: a column that keeps going after the
                // last row of its branch hangs in the air beside the next branch, which reads as a
                // line that belongs nowhere.
                var beyond = inside
                    .Where(span => span.Top >= to && span.Top - to > 6)
                    .Select(span => span.Top)
                    .DefaultIfEmpty(0)
                    .Min();
                if (beyond > 0 && beyond - to > 6)
                {
                    holes.Add($"{row.Step.Type} d{row.Depth}: column {column} runs on to {beyond:0} past {to:0}");
                }
            }

            Assert.True(strays.Count == 0, "lines at a column of their own: " + string.Join(" | ", strays));
            Assert.True(holes.Count == 0, "columns with a hole: " + string.Join(" | ", holes));
            Ui.CloseAndAnswer(window);
        });
    }

    [Fact]
    public void A_folded_block_does_not_draw_the_line_down_to_the_steps_it_hides()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var loop = Step("control.repeat",
                new StepParameter { Name = "times", Kind = ActionParameterKind.Number, Value = "2" },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.delay")],
                });
            editor.AddStep(loop);

            var block = editor.Rows.First(row => ReferenceEquals(row.Step, loop));
            Assert.Contains(block.Guides, line => line.Tail);

            // The fold goes through the row, the way the chevron drives it: the rows are not rebuilt,
            // the children are simply not drawn, so the line that led to them has to go with them.
            block.IsExpanded = false;
            Assert.DoesNotContain(block.Guides, line => line.Tail);

            block.IsExpanded = true;
            Assert.Contains(block.Guides, line => line.Tail);
        });
    }

    [Fact]
    public void The_line_that_adds_a_branch_hangs_off_the_same_column_as_the_branches()
    {
        Ui.Run(() =>
        {
            var editor = new MacroEditorViewModel();
            var branch = Step("control.case",
                new StepParameter { Name = "values", Kind = ActionParameterKind.Variable, Value = "1" },
                new StepParameter
                {
                    Name = "body",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.note")],
                });
            editor.AddStep(Step("control.switch",
                new StepParameter { Name = "switchValue", Kind = ActionParameterKind.Variable, Value = "$mode" },
                new StepParameter
                {
                    Name = "cases",
                    Kind = ActionParameterKind.Steps,
                    Steps = [branch],
                },
                new StepParameter
                {
                    Name = "otherwise",
                    Kind = ActionParameterKind.Steps,
                    Steps = [Step("control.log")],
                }));

            var add = editor.Rows.Single(row => row.IsAddRow);

            // The foot of the table is a row of it, not a caption floating under it: the branch
            // column turns into it the way it turns into a branch...
            var elbow = Assert.Single(add.Guides, line => line.Elbow);
            Assert.Equal(-(StepRow.IndentWidth + StepRow.ChevronCenter), elbow.Left);

            // ...and carries on past it to the otherwise steps, which follow the branch list.
            Assert.True(elbow.Full);
        });
    }

    private static MacroStep Step(string type, params StepParameter[] parameters)
        => new() { Type = type, Parameters = [.. parameters] };
}
