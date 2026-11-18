using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The rows of the list and the watch panel beside them: widths, titles and names.
public partial class RunDebugTests
{
    /// <summary>
    /// The watch list at the window's narrowest: the copy button keeps its place, the value keeps
    /// its column, and neither is drawn over the other or pushed out of the panel.
    /// </summary>
    [Fact]
    public void The_watch_list_stays_inside_its_panel_when_it_is_narrow()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            var other = Count("other", "1");
            StepIds.Settle([set, other]);

            var window = new RunWindow([set, other]) { Width = 760 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var row = window.GetVisualDescendants().OfType<Grid>()
                .First(grid => grid.DataContext is WatchedVariable);
            var button = row.Children.OfType<Button>().Single();
            var value = row.Children.OfType<SelectableTextBlock>().Single();

            Assert.True(button.Bounds.Right <= row.Bounds.Width + 0.5);
            Assert.True(value.Bounds.Right <= button.Bounds.X + 0.5);
            Assert.True(value.Bounds.Width > 0);

            // The title of the value column stands over the values, not over the copy buttons: the
            // two grids carry the same columns, the button column included.
            var title = window.GetVisualDescendants().OfType<TextBlock>()
                .First(text => text.Text == Strings.Get("Variable.Value"));
            var leftOfTitle = title.TranslatePoint(new Point(0, 0), window)!.Value.X;
            var leftOfValue = value.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.Equal(leftOfTitle, leftOfValue, 1);

            window.Close();
        });
    }

    /// <summary>
    /// The title over the name column and the name under it start at the same place, so the column
    /// reads as one column rather than as a heading shifted off its values.
    /// </summary>
    [Fact]
    public void The_name_column_and_its_title_start_together()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            StepIds.Settle([set]);

            var window = new RunWindow([set]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var texts = window.GetVisualDescendants().OfType<TextBlock>().ToList();
            var title = texts.First(text => text.Text == Strings.Get("Variable.Name"));
            var value = texts.First(text => text.Text == "total");

            var leftOfTitle = title.TranslatePoint(new Point(0, 0), window)!.Value.X;
            var leftOfValue = value.TranslatePoint(new Point(0, 0), window)!.Value.X;
            Assert.Equal(leftOfTitle, leftOfValue, 1);

            window.Close();
        });
    }

    /// <summary>
    /// A breakpoint can wait for an arrival: the first two times past it the run goes on, the third
    /// time it stops, which is what debugging the eighty-seventh pass of a loop asks for.
    /// </summary>
    [Fact]
    public void A_breakpoint_can_stop_on_a_given_arrival()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            StepIds.Settle([set]);

            var window = new RunWindow([set]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            var row = viewModel.Steps.First();
            viewModel.SetBreakpointRule(row, new BreakpointRule { HitTimes = 3 });

            Assert.False(viewModel.StopsHere(row.Id));
            Assert.False(viewModel.StopsHere(row.Id));
            Assert.True(viewModel.StopsHere(row.Id));
            Assert.True(viewModel.StopsHere(row.Id));

            window.Close();
        });
    }

    /// <summary>
    /// A condition nobody can work out is not a reason to stop: the run goes on and the log says what
    /// the question was, where a breakpoint that stops at random is worse than one that does not.
    /// </summary>
    [Fact]
    public void A_condition_that_cannot_be_read_does_not_stop_the_run()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            StepIds.Settle([set]);

            var window = new RunWindow([set]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            var row = viewModel.Steps.First();
            viewModel.SetBreakpointRule(row, new BreakpointRule { Expression = Formula.Binary("==", Formula.Name("nowhere"), Formula.Number(1)) });

            Assert.False(viewModel.StopsHere(row.Id));
            Assert.Contains(viewModel.Lines, line => line.Text.Contains("断点", StringComparison.Ordinal));

            window.Close();
        });
    }

    /// <summary>
    /// A rule that asks nothing is the same breakpoint as one set without a panel: it stops every
    /// time and the row says nothing about a condition.
    /// </summary>
    [Fact]
    public void A_rule_that_asks_nothing_leaves_the_row_without_a_note()
    {
        Ui.Run(() =>
        {
            var set = Count("total", "7");
            StepIds.Settle([set]);

            var window = new RunWindow([set]);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var viewModel = (RunViewModel)window.DataContext!;
            var row = viewModel.Steps.First();
            viewModel.SetBreakpointRule(row, new BreakpointRule { HitTimes = 1 });

            Assert.True(row.HasBreakpoint);
            Assert.Equal(string.Empty, row.ConditionNote);
            Assert.Null(viewModel.BreakpointRuleOf(row.Id));

            viewModel.SetBreakpointRule(row, new BreakpointRule { HitTimes = 2 });
            Assert.Equal(Strings.Format("Run.BreakpointNoteHits", 2), row.ConditionNote);

            window.Close();
        });
    }

    /// <summary>
    /// The panel writes its condition the way every other condition field does: the names and the
    /// functions complete as they are typed, and the line under the box says what it reads or what is
    /// wrong with it.
    /// </summary>
    [Fact]
    public void The_breakpoint_panel_reads_its_expression_and_keeps_the_count()
    {
        Ui.Run(() =>
        {
            var choices = new List<VariableChoice>
            {
                new() { Name = "total", Scope = VariableScope.Local },
            };
            var panel = new BreakpointViewModel(null, choices)
            {
                HitTimes = 4,
            };

            Assert.Contains("total", panel.ExpressionSuggestions);
            Assert.Contains(panel.ExpressionSuggestions,
                suggestion => suggestion.EndsWith("(", StringComparison.Ordinal));

            panel.Expression = Formula.Binary(">", Formula.Name("total"), Formula.Hole);
            Assert.True(panel.ShowExpressionError);
            Assert.False(panel.ShowExpressionSuccess);

            panel.Expression = Formula.Binary(">", Formula.Name("total"), Formula.Number(3));
            Assert.False(panel.ShowExpressionError);
            Assert.True(panel.ShowExpressionSuccess);

            panel.SaveCommand.Execute(null);
            Assert.Equal(BreakpointViewModel.Ending.Save, panel.How);
            Assert.NotNull(panel.Rule);
            Assert.Equal(4, panel.Rule!.HitTimes);
            Assert.Equal("total > 3", Expression.Render(panel.Rule.Expression));
        });
    }
}
