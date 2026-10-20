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
