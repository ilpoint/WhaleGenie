using System.Reflection;
using Avalonia.Media;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// Every icon in the interface comes from one set, drawn on one grid. A path that does not
/// parse throws when its property is first touched, which is while a window is being built, so
/// these tests touch all of them here instead and check they stay inside the grid.
/// </summary>
public class AppIconTests
{
    [Fact]
    public void Every_icon_parses_and_stays_on_the_grid()
    {
        var icons = typeof(AppIcons)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(Geometry))
            .ToArray();

        Assert.NotEmpty(icons);

        foreach (var property in icons)
        {
            var value = Assert.IsAssignableFrom<Geometry>(property.GetValue(null));
            var bounds = value.Bounds;

            Assert.True(bounds.Width > 0 || bounds.Height > 0, $"{property.Name} has no bounds");
            Assert.InRange(bounds.Left, 0, AppIcons.ViewBox + 0.01);
            Assert.InRange(bounds.Top, 0, AppIcons.ViewBox + 0.01);
            Assert.InRange(bounds.Right, 0, AppIcons.ViewBox + 0.01);
            Assert.InRange(bounds.Bottom, 0, AppIcons.ViewBox + 0.01);
            Assert.True(
                Math.Max(bounds.Width, bounds.Height) > 4,
                $"{property.Name} is too small to read as an icon");
        }
    }

    [Fact]
    public void Every_category_has_an_icon()
    {
        foreach (var category in Enum.GetValues<ActionCategory>())
        {
            Assert.NotNull(ActionCatalog.IconFor(category));
        }
    }

    [Fact]
    public void The_palette_toolbar_has_an_icon_for_every_entry()
    {
        var palette = new MacroEditorViewModel().ActionPalette;

        Assert.Equal(15, palette.Count);
        Assert.All(palette, action => Assert.NotNull(action.Icon));
    }
}
