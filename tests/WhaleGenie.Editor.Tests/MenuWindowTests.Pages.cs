using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// The page controls, the first page, and a child dialog over the menu.
public partial class MenuWindowTests
{
    [Fact]
    public void A_long_press_drag_moves_a_cell_without_running_it()
    {
        Ui.RunAsync(async () =>
        {
            MenuSlotRef? source = null;
            MenuSlotRef? target = null;
            var first = new MacroItem { Name = "first" };
            var second = new MacroItem { Name = "second" };
            var page = new MenuRuntimePage("common", false, string.Empty,
            [
                Cell(0, 0, first),
                Cell(0, 1, second),
            ]);
            var layout = new MenuRuntimeLayout([page],
                [new MenuRuntimePage("default", true, "Default", [])]);
            var window = new MacroMenuWindow(layout, Actions(move: (from, to) =>
            {
                source = from;
                target = to;
                return Task.FromResult<MenuRuntimeLayout?>(null);
            }), "notepad");
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cells = window.GetVisualDescendants().OfType<Button>()
                .Where(candidate => candidate.Classes.Contains("MenuCell")
                    && candidate.Tag is MenuSlotRef { Cell: not null })
                .OrderBy(candidate => ((MenuSlotRef)candidate.Tag!).Column)
                .ToList();
            var from = Waypoint(window, cells[0]);
            var to = Waypoint(window, cells[1]);

            Assert.Equal(TimeSpan.FromMilliseconds(400), MacroMenuWindow.CellDragDelay);
            window.BeginCellDragForCheck((MenuSlotRef)cells[0].Tag!, cells[0]);
            await window.DropCellForCheckAsync(to);

            Assert.Equal("first", source?.Cell?.Macro.Name);
            Assert.Equal("second", target?.Cell?.Macro.Name);
            Assert.False(window.Answer.IsCompleted);
            Assert.True(window.IsVisible);
            window.Close();
            return true;
        });
    }

    [Fact]
    public void Page_controls_are_visible_without_an_edit_mode()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();

            Assert.NotNull(FindButton(window, "AddCommonPageButton"));
            Assert.NotNull(FindButton(window, "AddContextPageButton"));
            window.Close();
        });
    }
}
