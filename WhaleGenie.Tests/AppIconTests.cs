using System.Reflection;
using Lucide.Avalonia;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>Every semantic interface icon resolves to one enum in the Lucide set.</summary>
public class AppIconTests
{
    [Fact]
    public void Every_app_icon_is_a_defined_lucide_icon()
    {
        var icons = typeof(AppIcons)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(property => property.PropertyType == typeof(LucideIconKind))
            .ToArray();

        Assert.NotEmpty(icons);
        foreach (var property in icons)
        {
            var value = Assert.IsType<LucideIconKind>(property.GetValue(null));
            Assert.True(Enum.IsDefined(value), $"{property.Name} is not a Lucide icon");
        }
    }

    [Fact]
    public void Every_icon_can_be_created_by_the_interface_control()
    {
        Ui.Run(() =>
        {
            foreach (var icon in Enum.GetValues<LucideIconKind>())
            {
                var control = new IconView { Data = icon };
                Assert.Equal(icon, control.Data);
            }
        });
    }

    [Fact]
    public void Icon_layout_width_and_height_drive_the_lucide_canvas()
    {
        Ui.Run(() =>
        {
            var small = new IconView { Data = AppIcons.X, Width = 10, Height = 10 };
            Assert.Equal(10, small.Size);

            var nonSquare = new IconView { Data = AppIcons.Minus, Width = 10, Height = 8 };
            Assert.Equal(8, nonSquare.Size);

            var widthOnly = new IconView { Data = AppIcons.X, Width = 14 };
            Assert.Equal(14, widthOnly.Size);
        });
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

    [Fact]
    public void The_menu_icon_picker_offers_a_broad_set_of_named_lucide_icons()
    {
        Assert.True(MenuIconCatalog.Options.Count >= 70);
        Assert.Equal(MenuIconCatalog.Options.Count,
            MenuIconCatalog.Options.Select(option => option.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(MenuIconCatalog.Options, option =>
        {
            Assert.True(Enum.IsDefined(option.Icon));
            Assert.NotEmpty(option.Key);
            Assert.NotEmpty(option.Label);
        });
    }
}
