using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What "this pixel is the colour" means. The walking of a picture reads four bytes at a time and
/// adds up three squares rather than working out a distance per pixel, so these keep the fast way
/// and the plain way saying the same thing at the edges: a pixel exactly at the limit, a tolerance
/// of nothing, a tolerance of everything, and a screenful of colours in between.
/// </summary>
public class ColourNearnessTests
{
    private static readonly PixelColor Wanted = PixelColor.Parse("#3A7BC8");

    /// <summary>A picture of every colour whose channels are one of a few levels: a 16×16×16 cube.</summary>
    private static ImageFrame EveryDifference()
    {
        var levels = Enumerable.Range(0, 16).Select(step => (byte)(step * 17)).ToArray();
        var colours = new List<PixelColor>();
        foreach (var red in levels)
        {
            foreach (var green in levels)
            {
                foreach (var blue in levels)
                {
                    colours.Add(new PixelColor(red, green, blue));
                }
            }
        }

        var bytes = new byte[colours.Count * 4];
        for (var index = 0; index < colours.Count; index++)
        {
            bytes[(index * 4) + 0] = colours[index].B;
            bytes[(index * 4) + 1] = colours[index].G;
            bytes[(index * 4) + 2] = colours[index].R;
            bytes[(index * 4) + 3] = 255;
        }

        return new ImageFrame(colours.Count, 1, bytes);
    }

    /// <summary>The plain reading of the rule: a colour compared with another one, one at a time.</summary>
    private static List<int> Plainly(ImageFrame frame, double tolerance)
    {
        var found = new List<int>();
        for (var x = 0; x < frame.Width; x++)
        {
            if (frame[x, 0].Matches(Wanted, tolerance))
            {
                found.Add(x);
            }
        }

        return found;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(12.5)]
    [InlineData(33)]
    [InlineData(58)]
    [InlineData(99.9)]
    [InlineData(100)]
    [InlineData(150)]
    public void Walking_a_picture_finds_the_pixels_a_colour_is_compared_with_one_at_a_time(
        double tolerance)
    {
        var frame = EveryDifference();
        var plainly = Plainly(frame, tolerance);

        Assert.Equal(plainly.Count, PixelSearch.Count(frame, Wanted, tolerance));

        // Asked for every one of them, the walk hands back the same pixels in the same order.
        var all = PixelSearch.Find(frame, Wanted, tolerance, frame.Width);
        Assert.Equal(plainly, all.Select(at => at.X));

        // Asked for the first few, it stops where the plain walk would have stopped reading.
        var some = PixelSearch.Find(frame, Wanted, tolerance, 5);
        Assert.Equal(plainly.Take(5), some.Select(at => at.X));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(58)]
    [InlineData(100)]
    public void The_nearest_pixels_come_back_in_the_order_their_distance_puts_them(double tolerance)
    {
        var frame = EveryDifference();
        var plainly = Plainly(frame, tolerance)
            .Select(at => (Distance: frame[at, 0].DistanceTo(Wanted), At: at))
            .OrderBy(candidate => candidate.Distance)
            .Select(candidate => candidate.At)
            .Take(20)
            .ToList();

        var nearest = PixelSearch.Find(frame, Wanted, tolerance, 20, surest: true);
        Assert.Equal(plainly, nearest.Select(at => at.X));
    }

    [Fact]
    public void A_pattern_needs_its_colours_where_they_were_asked_for()
    {
        // The colour sits at 0,0 with both of the colours the pattern asks for around it, and again
        // at 2,0 where the colour to its right is not the one asked for: that is what tells a
        // pattern from something on screen that merely has the colour in it.
        var frame = Picture(
            "#3A7BC8,#FF0000,#3A7BC8,#0000FF",
            "#3A7BC7,#FF0100,#3A7BC7,#FF0100");
        var around = new List<ColourStep>
        {
            new(0, 1, PixelColor.Parse("#3A7BC7")),
            new(1, 0, PixelColor.Parse("#FF0000")),
        };

        var found = PixelSearch.FindPattern(frame, Wanted, 5, around, 10);

        var at = Assert.Single(found);
        Assert.Equal(0, at.X);
        Assert.Equal(0, at.Y);
    }

    /// <summary>A picture from rows of #RRGGBB colours, one string per row.</summary>
    private static ImageFrame Picture(params string[] rows)
    {
        var width = rows[0].Split(',').Length;
        var bytes = new byte[width * rows.Length * 4];
        for (var y = 0; y < rows.Length; y++)
        {
            var cells = rows[y].Split(',');
            for (var x = 0; x < width; x++)
            {
                var colour = PixelColor.Parse(cells[x]);
                var at = ((y * width) + x) * 4;
                bytes[at] = colour.B;
                bytes[at + 1] = colour.G;
                bytes[at + 2] = colour.R;
                bytes[at + 3] = 255;
            }
        }

        return new ImageFrame(width, rows.Length, bytes);
    }
}
