using Lucide.Avalonia;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>Every icon the interface can name has to be drawable and sized by its own box.</summary>
public class AppIconTests
{
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
            var small = new IconView { Data = LucideIconKind.X, Width = 10, Height = 10 };
            Assert.Equal(10, small.Size);

            var nonSquare = new IconView { Data = LucideIconKind.Minus, Width = 10, Height = 8 };
            Assert.Equal(8, nonSquare.Size);

            var widthOnly = new IconView { Data = LucideIconKind.X, Width = 14 };
            Assert.Equal(14, widthOnly.Size);
        });
    }

    [Fact]
    public void The_menu_icon_picker_offers_every_named_lucide_icon()
    {
        Assert.Equal(Enum.GetValues<LucideIconKind>().Length, MenuIconCatalog.Options.Count);
        Assert.Equal(MenuIconCatalog.Options.Count,
            MenuIconCatalog.Options.Select(option => option.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(MenuIconCatalog.Options, option =>
        {
            Assert.True(Enum.IsDefined(option.Kind));
            Assert.NotEmpty(option.Key);
            Assert.Equal(option.Key, option.Label);
        });
    }
}
