namespace WhaleGenie.GraphicsCapture.Tests;

/// <summary>
/// The names the ways of taking a picture go by. They are written into the settings file and read
/// back out of it, and they are what says which way a step's picture was really taken with, so a
/// name that does not come back as the way it stands for is a setting that quietly means something
/// else.
/// </summary>
public class CaptureWayTests
{
    [Fact]
    public void Every_way_is_written_and_read_back_as_itself()
    {
        foreach (var way in new[]
                 {
                     CaptureWay.Gdi, CaptureWay.PrintWindow, CaptureWay.GraphicsCapture,
                     CaptureWay.DesktopDuplication,
                 })
        {
            Assert.Equal(way, CaptureWayNames.Read(CaptureWayNames.Written(way)));
        }
    }

    /// <summary>
    /// The automatic choice is a question rather than a way of taking a picture, so it is written as
    /// nothing at all, and a name nobody knows reads as it: a half-written setting looks at
    /// something rather than at nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("GDI")]  // read as the same way however it was spelled
    public void What_is_not_a_way_reads_as_the_automatic_choice(string written)
        => Assert.Equal(written == "GDI" ? CaptureWay.Gdi : CaptureWay.Auto,
            CaptureWayNames.Read(written));

    [Fact]
    public void The_automatic_choice_is_written_as_nothing()
        => Assert.Equal(string.Empty, CaptureWayNames.Written(CaptureWay.Auto));
}
