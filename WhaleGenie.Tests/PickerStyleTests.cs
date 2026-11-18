using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace WhaleGenie.Tests;

/// <summary>
/// The picker overlays are the one part of the palette that must not follow the theme. The five
/// pickers stand on top of the screen while somebody reads pixels off it, so a scrim that got
/// lighter in the light variant would change what they see and what a screenshot contains.
///
/// This is the only place the family can fail, and it cannot fail visibly: the tokens are written
/// outside the theme dictionaries, which looks like an accident to the next person, and moving them
/// inside would change nothing until a picker was used in the other variant.
/// </summary>
[Collection("Theme")]
public class PickerStyleTests
{
    [Fact]
    public void The_picker_overlays_are_the_same_in_both_variants()
    {
        Ui.Run(() =>
        {
            foreach (var key in new[] { "PickerScrim", "PickerTargetScrim", "PickerReadout" })
            {
                var dark = Colour(key, ThemeVariant.Dark);
                var light = Colour(key, ThemeVariant.Light);
                Assert.Equal(dark, light);
            }
        });
    }

    private static Color Colour(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
    }
}
