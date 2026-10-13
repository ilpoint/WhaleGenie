namespace WhaleGenie.GraphicsCapture.Tests;

/// <summary>
/// Which part of a rectangle of the desk comes from which display. This is what a desk of several
/// displays turns on, and it is worked out without a screen being there: the piece each display
/// contributes, where in the picture it lands, and what a rectangle that is on no display at all
/// comes to.
/// </summary>
public class ScreenLayoutTests
{
    /// <summary>The usual two 1920×1080 displays side by side, the second one to the right.</summary>
    private static readonly ScreenArea Left = new(0, 0, 1920, 1080);
    private static readonly ScreenArea Right = new(1920, 0, 1920, 1080);

    private static readonly ScreenLayout SideBySide = new(new ScreenArea(0, 0, 3840, 1080),
        [new ScreenDisplay(Left, true), new ScreenDisplay(Right, false)]);

    /// <summary>A second display to the left of the primary one, which is where the negative numbers start.</summary>
    private static readonly ScreenLayout LeftOfCentre = new(new ScreenArea(-1920, 0, 3840, 1080),
        [new ScreenDisplay(Left, true), new ScreenDisplay(new ScreenArea(-1920, 0, 1920, 1080), false)]);

    [Fact]
    public void A_rectangle_on_one_display_is_one_piece()
    {
        var pieces = ScreenPlacement.Pieces(SideBySide, 100, 200, 50, 40);

        var piece = Assert.Single(pieces);
        Assert.Equal(Left, piece.Display);
        Assert.Equal((100, 200, 0, 0, 50, 40),
            (piece.SourceX, piece.SourceY, piece.ToX, piece.ToY, piece.Width, piece.Height));
    }

    /// <summary>
    /// A rectangle straddling the seam is exactly two pieces, and the seam is followed exactly where
    /// it falls: the part left of it comes off the left-hand display and the part right of it off the
    /// right-hand one, each landing where it belongs in the picture that comes back.
    /// </summary>
    [Fact]
    public void A_rectangle_across_the_seam_comes_from_both_displays()
    {
        var pieces = ScreenPlacement.Pieces(SideBySide, 1900, 10, 40, 20);

        Assert.Equal(2, pieces.Count);

        Assert.Equal(Left, pieces[0].Display);
        Assert.Equal((1900, 10, 0, 0, 20, 20),
            (pieces[0].SourceX, pieces[0].SourceY, pieces[0].ToX, pieces[0].ToY, pieces[0].Width,
                pieces[0].Height));

        // The right-hand display's own corner is 1920 across, so the part of it that is wanted
        // starts 20 pixels in from it, and lands 20 pixels into the picture.
        Assert.Equal(Right, pieces[1].Display);
        Assert.Equal((0, 10, 20, 0, 20, 20),
            (pieces[1].SourceX, pieces[1].SourceY, pieces[1].ToX, pieces[1].ToY, pieces[1].Width,
                pieces[1].Height));
    }

    /// <summary>
    /// A display to the left of the primary one hands over its own picture, which starts at a
    /// negative corner: the source coordinate is counted from that display's corner, not from the
    /// corner of the desk.
    /// </summary>
    [Fact]
    public void A_display_left_of_the_primary_one_is_read_from_its_own_corner()
    {
        var pieces = ScreenPlacement.Pieces(LeftOfCentre, -100, 5, 50, 10);

        var piece = Assert.Single(pieces);
        Assert.Equal(new ScreenArea(-1920, 0, 1920, 1080), piece.Display);
        Assert.Equal((1820, 5, 0, 0, 50, 10),
            (piece.SourceX, piece.SourceY, piece.ToX, piece.ToY, piece.Width, piece.Height));
    }

    /// <summary>A desk of one display is what every machine had before, and it stays one piece.</summary>
    [Fact]
    public void One_display_is_still_one_piece()
    {
        var alone = new ScreenLayout(new ScreenArea(0, 0, 2560, 1440),
            [new ScreenDisplay(new ScreenArea(0, 0, 2560, 1440), true)]);

        var piece = Assert.Single(ScreenPlacement.Pieces(alone, 0, 0, 2560, 1440));
        Assert.Equal((0, 0, 0, 0, 2560, 1440),
            (piece.SourceX, piece.SourceY, piece.ToX, piece.ToY, piece.Width, piece.Height));
    }

    /// <summary>A rectangle hanging off the edge of the desk keeps the part that is on it.</summary>
    [Fact]
    public void A_rectangle_hanging_off_the_desk_keeps_the_part_that_is_on_it()
    {
        var pieces = ScreenPlacement.Pieces(SideBySide, 3820, 1070, 40, 40);

        var piece = Assert.Single(pieces);
        Assert.Equal(Right, piece.Display);
        Assert.Equal((1900, 1070, 0, 0, 20, 10),
            (piece.SourceX, piece.SourceY, piece.ToX, piece.ToY, piece.Width, piece.Height));
    }

    [Fact]
    public void A_rectangle_no_display_holds_is_refused()
    {
        Assert.Throws<EmptyRegionException>(
            () => ScreenPlacement.Pieces(SideBySide, 5000, 5000, 100, 100));
    }

    [Fact]
    public void The_display_a_point_is_on_is_the_one_that_holds_it()
    {
        Assert.Equal(Left, SideBySide.At(100, 100).Bounds);
        Assert.Equal(Right, SideBySide.At(2000, 100).Bounds);

        // A point on no display at all comes back as the primary one rather than as nothing.
        Assert.Equal(Left, SideBySide.At(-50, 100).Bounds);
    }
}
