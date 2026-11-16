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

// Where the parts sit: the square pictures, the divider and the search over it.
public partial class MenuWindowTests
{
    [Fact]
    public void Square_images_fill_the_cell_and_other_images_do_not()
    {
        Assert.True(MacroMenuWindow.FillsCell(new PixelSize(128, 128)));
        Assert.False(MacroMenuWindow.FillsCell(new PixelSize(128, 72)));
    }

    [Fact]
    public void The_menu_places_its_middle_divider_at_the_pointer_height()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");

            Assert.Equal(new PixelPoint(512, 45),
                window.PlaceNear(new PixelPoint(500, 400),
                    new PixelRect(0, 0, 1000, 1200), 1));
            Assert.Equal(new PixelPoint(618, 133),
                window.PlaceNear(new PixelPoint(980, 700),
                    new PixelRect(0, 0, 1000, 800), 1));
            Assert.Equal(new PixelPoint(-1888, 0),
                window.PlaceNear(new PixelPoint(-1900, 100),
                    new PixelRect(-1920, 0, 1920, 1080), 1));
        });
    }

    [Fact]
    public void Opening_search_keeps_the_divider_under_the_pointer()
    {
        Ui.Run(() =>
        {
            var window = new MacroMenuWindow(Layout(), Actions(), "notepad");
            window.Show();
            var cursor = new PixelPoint(500, 400);
            var area = new PixelRect(0, 0, 1000, 1200);

            Assert.Equal(667, window.Height);
            Assert.Equal(355, window.DividerCenterOffset);
            Assert.Equal(cursor.Y,
                window.PlaceNear(cursor, area, 1).Y + window.DividerCenterOffset);

            window.SetSearchOpen(true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(667, window.Height);
            Assert.Equal(355, window.DividerCenterOffset);
            Assert.Equal(cursor.Y,
                window.PlaceNear(cursor, area, 1).Y + window.DividerCenterOffset);
            window.Close();
        });
    }

    private static MenuRuntimeLayout Layout(string? description = null,
        MenuIconReference? icon = null)
    {
        var macro = new MacroItem { Name = "one", Description = description ?? string.Empty };
        return new MenuRuntimeLayout(
        [
            new MenuRuntimePage("common", false, string.Empty, [Cell(0, 0, macro, icon)]),
        ],
        [new MenuRuntimePage("default", true, "Default", [])]);
    }

    private static MenuRuntimeCell Cell(int row, int column, MacroItem macro,
        MenuIconReference? icon = null)
    {
        var entry = new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = macro.Name,
            MenuIcon = icon,
        };
        return new MenuRuntimeCell(row, column, icon, entry, macro);
    }

    private static Button OccupiedCell(MacroMenuWindow window)
        => window.GetVisualDescendants().OfType<Button>().First(candidate =>
            candidate.Classes.Contains("MenuCell")
            && candidate.Tag is MenuSlotRef { Cell: not null });

    private static Button FindButton(MacroMenuWindow window, string name)
        => window.GetVisualDescendants().OfType<Button>().Single(button =>
            button.Name == name);

    private static Point Waypoint(Window window, Visual control)
        => control.TranslatePoint(
            new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window) ?? default;

    private static MenuWindowActions Actions(Action? restore = null,
        Func<MenuSlotRef, MenuSlotRef, Task<MenuRuntimeLayout?>>? move = null,
        Action<MacroItem>? edit = null,
        Action<MacroItem>? debug = null,
        Func<Task<MenuRuntimeLayout?>>? addRule = null,
        Func<Task<bool>>? block = null)
        => new(
            edit ?? (_ => { }),
            debug ?? (_ => { }),
            () => { },
            restore ?? (() => { }),
            addRule ?? (() => Task.FromResult<MenuRuntimeLayout?>(null)),
            block ?? (() => Task.FromResult(false)),
            () => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            _ => Task.FromResult<MenuRuntimeLayout?>(null),
            move ?? ((_, _) => Task.FromResult<MenuRuntimeLayout?>(null)));
}
