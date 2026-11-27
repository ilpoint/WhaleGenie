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
        Assert.Equal(ImageFrame.Empty,
            OcrPreprocess.ByColour(ImageFrame.Empty, new PixelColor(255, 255, 255), 10));
    }

    [Fact]
    public void The_recipes_are_the_five_the_interface_offers_in_that_order()
        => Assert.Equal(["none", "greyscale", "binarize", "upscale", "upscaleBinarize"],
            OcrPreprocess.Recipes);

    [Fact]
    public void A_pixel_exactly_at_the_average_counts_as_writing()
    {
        // The average of 50, 100 and 150 is 100, which is one of the pixels: "at or above the cut"
        // is what keeps it, and a reader that only took what was above it would drop the writing.
        var frame = Frame(width: 3, height: 1, (50, 50, 50), (100, 100, 100), (150, 150, 150));

        var black = OcrPreprocess.Binarize(frame);

        Assert.Equal(0, black.Bgra[0]);
        Assert.Equal(255, black.Bgra[4]);
        Assert.Equal(255, black.Bgra[8]);
    }

    [Fact]
    public void Making_a_picture_bigger_needs_a_scale_of_two_or_more()
    {
        var frame = Frame(width: 2, height: 1, (255, 0, 0), (0, 0, 255));

        Assert.Same(frame, OcrPreprocess.Upscale(frame, 1));
        Assert.Same(frame, OcrPreprocess.Upscale(frame, 0));
        Assert.Same(frame, OcrPreprocess.Upscale(frame, -2));
    }

    [Fact]
    public void A_scale_of_three_repeats_each_pixel_three_ways()
    {
        var frame = Frame(width: 2, height: 1, (255, 0, 0), (0, 0, 255));

        var bigger = OcrPreprocess.Upscale(frame, 3);

        Assert.Equal(6, bigger.Width);
        Assert.Equal(3, bigger.Height);
        Assert.Equal(255, bigger[2, 2].R);
        Assert.Equal(0, bigger[2, 2].B);
        Assert.Equal(0, bigger[3, 0].R);
        Assert.Equal(255, bigger[3, 0].B);
    }

    /// <summary>
    /// Writing drawn over a picture is where the reading model is handed too much; keeping only the
    /// colour the writing is in is what hands it the writing. What is kept stays as bright as it
    /// was, because a model reads edges better than a flat silhouette.
    /// </summary>
    [Fact]
    public void Only_the_colour_the_writing_is_in_is_kept()
    {
        var frame = Frame(width: 4, height: 1,
            (255, 255, 255),  // the writing
            (200, 200, 200),  // the same writing, a shade off: still writing
            (0, 90, 200),     // the picture behind it
            (0, 0, 0));       // the outline round the writing

        var writing = OcrPreprocess.ByColour(frame, new PixelColor(255, 255, 255), 25);

        // The writing keeps its own brightness; the picture behind it and the outline are both
        // flattened to one flat white, which leaves the model nothing to read but the writing.
        Assert.Equal(255, writing[0, 0].R);
        Assert.Equal(200, writing[1, 0].R);
        Assert.Equal(255, writing[2, 0].R);
        Assert.Equal(255, writing[3, 0].R);

        // Nothing is thrown away but the colour: the writing stays where it was on the screen, so
        // the positions that come back are still screen positions.
        Assert.Equal(4, writing.Width);
        Assert.Equal(1, writing.Height);
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
