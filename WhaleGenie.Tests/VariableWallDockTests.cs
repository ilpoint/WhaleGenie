using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The wall stands in a 360-pixel column beside the macro, so its own chrome has to be spent
/// carefully: a dock that wraps into two lines and a panel of description that repeats the tooltip
/// both take height away from the only thing the wall is for — the variables themselves.
/// </summary>
public class VariableWallDockTests
{
    [Theory]
    [InlineData(Language.Chinese)]
    [InlineData(Language.English)]
    public void The_group_chips_stay_on_one_line(Language language)
    {
        var (dock, chip, content, room) = Ui.Run(() =>
        {
            // Five groups, one line, in either language: English spells the same five longer than
            // Chinese does, so the row is measured where it is longest.
            var stored = Strings.Current.Language;
            Strings.Current.Language = language;
            var view = new VariableWall { DataContext = new VariableWallViewModel(Few()) };
            var window = new Window { Width = 336, Height = 300, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var groups = view.FindControl<ListBox>("VariableGroups")!;
            var items = groups.GetVisualDescendants().OfType<ListBoxItem>().ToList();
            var measured = (groups.Bounds.Height, items[0].Bounds.Height,
                items.Max(item => item.Bounds.Right) - items.Min(item => item.Bounds.Left),
                groups.Bounds.Width);
            window.Close();
            Strings.Current.Language = stored;
            return measured;
        });

        // One line is one chip; a wrapped dock is two of them plus the spacing between rows.
        Assert.InRange(dock, chip, chip * 1.6);

        // And all five are on show: a row wider than the dock has a chip nobody can see without
        // scrolling it sideways, which is the same complaint in a different shape.
        Assert.True(content <= room + 0.5,
            $"the chips take {content:0.#} pixels and the dock has {room:0.#}");
    }

    [Fact]
    public void The_wall_beside_the_macro_has_no_details_panel()
    {
        var (besideTheMacro, inAPicker) = Ui.Run(() =>
        (
            Show(new VariableCenterViewModel().Wall),
            Show(new VariableWallViewModel(Few()))
        ));

        // The editor's wall gives the band back to the list; a picker keeps it, because there the
        // step names in it are buttons that jump to the step.
        Assert.False(besideTheMacro.Details);
        Assert.True(inAPicker.Details);

        // Given back, not merely hidden: the list is that band taller.
        Assert.True(besideTheMacro.List >= inAPicker.List + 60,
            $"the list beside the macro is {besideTheMacro.List:0.#} tall and the picker's {inAPicker.List:0.#}");
    }

    [Fact]
    public void What_the_details_panel_used_to_say_is_in_the_hover_text()
    {
        var choice = VariableChoices
            .For([Writes(), Reads("这次有 $total 件", "读合计"), Reads("打印 $total", "写报表")])
            .First(row => row.Name == "total");

        var text = choice.HoverText;

        Assert.Contains(Strings.Get("VarWall.UsedBy"), text, System.StringComparison.Ordinal);
        foreach (var use in choice.Uses)
        {
            Assert.Contains(use.Label, text, System.StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// What a wall looks like in a 336-pixel column: whether the details panel is there, and how tall
    /// the list it leaves is. Both are read off the view rather than the model.
    /// </summary>
    private static (bool Details, double List) Show(VariableWallViewModel wall)
    {
        var view = new VariableWall { DataContext = wall };
        var window = new Window { Width = 336, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var details = view.FindControl<ScrollViewer>("VariableDetails")!.IsVisible;
        var list = view.FindControl<ScrollViewer>("VariableList")!.Bounds.Height;
        window.Close();
        return (details, list);
    }

    private static IReadOnlyList<VariableChoice> Few() =>
    [
        new VariableChoice { Name = "total", Scope = VariableScope.Local, Type = "number", Description = "合计。" },
        new VariableChoice { Name = "rows", Scope = VariableScope.Local, Type = "record", Description = "行。" },
    ];

    /// <summary>The step that makes the variable; a step that defines it does not read it.</summary>
    private static MacroStep Writes() => new()
    {
        Type = "control.setVariable",
        Meta = new StepMeta(),
        Parameters =
        [
            new StepParameter { Name = "name", Kind = ActionParameterKind.Text, Value = "total" },
            new StepParameter { Name = "value", Kind = ActionParameterKind.Text, Value = "1" },
        ],
    };

    private static MacroStep Reads(string expression, string label) => new()
    {
        Type = "control.log",
        Meta = new StepMeta { Comment = label },
        Parameters = [new StepParameter { Name = "message", Kind = ActionParameterKind.Text, Value = expression }],
    };
}
