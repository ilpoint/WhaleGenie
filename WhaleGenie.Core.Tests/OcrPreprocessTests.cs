using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Tidying a picture up before it is read. The frames here are built by hand — a light pixel and a
/// dark pixel are all a test needs — so the rules can be checked without a screen.
/// </summary>
public class OcrPreprocessTests
{
    [Fact]
    public void Grey_takes_the_colour_out_and_leaves_the_brightness()
    {
        var frame = Frame(width: 2, height: 1,
            (0, 0, 255),     // pure blue
            (0, 255, 0));    // pure green

        var grey = OcrPreprocess.Greyscale(frame);

        // Blue reads dark and green reads bright: that is what the eye sees, and it is why the
        // parts are not simply averaged.
        Assert.Equal(29, grey.Bgra[0]);
        Assert.Equal(grey.Bgra[0], grey.Bgra[2]);
        Assert.Equal(150, grey.Bgra[4]);
        Assert.Equal(grey.Bgra[4], grey.Bgra[6]);
        Assert.Equal(255, grey.Bgra[3]);
        Assert.Equal(255, grey.Bgra[7]);
    }

    [Fact]
    public void Black_and_white_is_cut_at_the_pictures_own_average()
    {
        // A dark screen: the writing is lighter than the background, so the light pixels become
        // white and the dark ones black, whichever way round the screen happens to be.
        var dark = Frame(width: 4, height: 1,
            (10, 10, 10), (10, 10, 10), (10, 10, 10), (200, 200, 200));

        var black = OcrPreprocess.Binarize(dark);

        Assert.Equal(0, black.Bgra[0]);
        Assert.Equal(0, black.Bgra[8]);
        Assert.Equal(255, black.Bgra[12]);
    }

    [Fact]
    public void Making_a_picture_bigger_repeats_its_pixels()
    {
        var frame = Frame(width: 2, height: 2,
            (255, 0, 0), (0, 0, 0),
            (0, 0, 0), (0, 255, 0));

        var bigger = OcrPreprocess.Upscale(frame, 2);

        Assert.Equal(4, bigger.Width);
        Assert.Equal(4, bigger.Height);
        Assert.Equal(255, bigger[0, 0].R);
        Assert.Equal(255, bigger[1, 1].R);
        Assert.Equal(0, bigger[2, 0].R);
        Assert.Equal(255, bigger[2, 2].G);
    }

    [Theory]
    [InlineData("none", 1)]
    [InlineData("", 1)]
    [InlineData("greyscale", 1)]
    [InlineData("binarize", 1)]
    [InlineData("upscale", 2)]
    [InlineData("upscaleBinarize", 2)]
    [InlineData(" Upscale ", 2)]
    public void A_recipe_says_how_much_bigger_the_picture_was_made(string recipe, int scale)
    {
        var frame = Frame(width: 2, height: 1, (10, 10, 10), (200, 200, 200));

        var (prepared, applied) = OcrPreprocess.Apply(frame, recipe);

        Assert.Equal(scale, applied);
        Assert.Equal(2 * scale, prepared.Width);
    }

    [Fact]
    public void Every_recipe_the_engine_offers_can_be_applied()
    {
        var frame = Frame(width: 2, height: 1, (10, 10, 10), (200, 200, 200));

        foreach (var recipe in OcrPreprocess.Recipes)
        {
            var (prepared, _) = OcrPreprocess.Apply(frame, recipe);

            Assert.False(prepared.IsEmpty, $"{recipe} left nothing to read");
        }
    }

    [Fact]
    public void A_frame_with_nothing_in_it_is_left_alone()
    {
        Assert.Equal(ImageFrame.Empty, OcrPreprocess.Greyscale(ImageFrame.Empty));
        Assert.Equal(ImageFrame.Empty, OcrPreprocess.Binarize(ImageFrame.Empty));
        Assert.Equal(ImageFrame.Empty, OcrPreprocess.Upscale(ImageFrame.Empty, 2));
    }

    /// <summary>A frame built out of the colours a test hands in, one pixel each.</summary>
    private static ImageFrame Frame(int width, int height, params (byte R, byte G, byte B)[] colours)
    {
        var pixels = new byte[width * height * 4];
        for (var at = 0; at < colours.Length; at++)
        {
            pixels[at * 4] = colours[at].B;
            pixels[at * 4 + 1] = colours[at].G;
            pixels[at * 4 + 2] = colours[at].R;
            pixels[at * 4 + 3] = 255;
        }

        return new ImageFrame(width, height, pixels);
    }
}
