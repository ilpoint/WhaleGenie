using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// Hovering a variable must not change the size of the list it sits in. The panel under it grows to
/// show what the variable is and which steps read it, and the list above would otherwise give up
/// the difference — reflowing every card under the pointer, which drops the hover, which returns the
/// size, which brings the card back: a bubble that flickers for as long as the pointer stays there.
/// </summary>
public class VariableWallHoverLayoutTests
{
    [Fact]
    public void Pointing_at_a_variable_does_not_resize_the_list()
    {
        var (before, after, cardsBefore, cardsAfter) = Ui.Run(() =>
        {
            var wall = new VariableWallViewModel(Many());
            var view = new VariableWall { DataContext = wall };
            var window = new Window { Width = 460, Height = 300, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var list = view.FindControl<ScrollViewer>("VariableList")!;
            var sizeBefore = (list.Bounds.Width, list.Bounds.Height);
            var countBefore = wall.Shown.Count;

            wall.Looking = wall.Shown.First(choice => choice.Uses.Count > 0);
            Dispatcher.UIThread.RunJobs();

            var sizeAfter = (list.Bounds.Width, list.Bounds.Height);
            window.Close();
            return (sizeBefore, sizeAfter, countBefore, wall.Shown.Count);
        });

        Assert.Equal(cardsBefore, cardsAfter);
        Assert.Equal(before, after);
    }

    /// <summary>A wall with more variables than fit, one of which several steps read.</summary>
    private static IReadOnlyList<VariableChoice> Many()
    {
        var choices = Enumerable.Range(1, 40)
            .Select(index => new VariableChoice
            {
                Name = $"value{index}",
                Scope = VariableScope.Local,
                Type = "text",
                Description = "写订单表时用到的值。",
                Source = "读订单表",
                Uses = index == 1
                    ? [Use("读订单表"), Use("写订单表"), Use("发通知")]
                    : [],
            })
            .ToList();
        return choices;
    }

    private static VariableUse Use(string label) => new(Step(label), label);

    private static MacroStep Step(string label) => new()
    {
        Type = "control.setVariable",
        Meta = new StepMeta { Comment = label },
        Parameters =
        [
            new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = "value1" },
            new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Value = "1" },
        ],
    };
}
