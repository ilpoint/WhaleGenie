using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The ways a window's picture can be taken, and the order the automatic choice tries them in. The
/// trying itself needs a real window to fall back through, so what is checked here is the order:
/// which way is reached for first, and what is left of it on a machine without graphics capture.
/// </summary>
public class CaptureWayTests
{
    [Fact]
    public void A_window_is_read_by_graphics_capture_first_and_off_the_screen_last()
    {
        CaptureMethod[] ways =
            [CaptureMethod.GraphicsCapture, CaptureMethod.PrintWindow, CaptureMethod.Gdi];

        Assert.Equal(ways, WindowsScreenDevice.Automatic(graphicsAvailable: true));
    }

    /// <summary>
    /// A machine that cannot do graphics capture — an old Windows, a remote session, no graphics
    /// driver — still reads a window, by asking the window to draw itself.
    /// </summary>
    [Fact]
    public void Without_graphics_capture_the_window_is_asked_to_draw_itself()
    {
        CaptureMethod[] ways = [CaptureMethod.PrintWindow, CaptureMethod.Gdi];

        Assert.Equal(ways, WindowsScreenDevice.Automatic(graphicsAvailable: false));
    }

    /// <summary>
    /// The display is read whole and the part that was asked for is cut out of it, so the cutting
    /// has to hand back the pixels of the part that was asked for and nothing else: a picture that
    /// is one row out is a click that lands one row out.
    /// </summary>
    [Fact]
    public void A_part_of_the_display_is_cut_out_where_it_was_asked_for()
    {
        var whole = Rows("00,01,02,03", "10,11,12,13", "20,21,22,23", "30,31,32,33");

        var part = GraphicsCapture.Clip(whole, 1, 2, 2, 2);

        Assert.Equal(2, part.Width);
        Assert.Equal(2, part.Height);
        Assert.Equal(Shader(Rows("21,22", "31,32")), Shader(part));
    }

    /// <summary>The whole of it asked for is the frame itself, not a copy of it.</summary>
    [Fact]
    public void Asking_for_the_whole_display_hands_back_the_frame_it_has()
    {
        var whole = Rows("00,01", "10,11");

        Assert.Same(whole, GraphicsCapture.Clip(whole, 0, 0, 2, 2));
    }

    /// <summary>A part of the screen that is not on it is refused rather than answered with less.</summary>
    [Theory]
    [InlineData(2, 0, 2, 2)]
    [InlineData(0, 2, 2, 2)]
    [InlineData(-1, 0, 2, 2)]
    [InlineData(0, 0, 0, 2)]
    public void A_part_that_runs_off_the_display_is_refused(int x, int y, int width, int height)
    {
        var whole = Rows("00,01,02", "10,11,12");

        Assert.Throws<DeviceActionException>(
            () => GraphicsCapture.Clip(whole, x, y, width, height));
    }

    /// <summary>A picture from rows of two-digit numbers, one per pixel, blue carrying the value.</summary>
    private static ImageFrame Rows(params string[] rows)
    {
        var width = rows[0].Split(',').Length;
        var bytes = new byte[width * rows.Length * 4];
        for (var y = 0; y < rows.Length; y++)
        {
            var cells = rows[y].Split(',');
            for (var x = 0; x < width; x++)
            {
                bytes[((((y * width) + x) * 4))] = (byte)int.Parse(cells[x]);
                bytes[(((y * width) + x) * 4) + 3] = 255;
            }
        }

        return new ImageFrame(width, rows.Length, bytes);
    }

    /// <summary>What a picture shows, written the way the rows above are.</summary>
    private static string Shader(ImageFrame frame)
    {
        var shown = new List<string>();
        for (var y = 0; y < frame.Height; y++)
        {
            var cells = new List<string>();
            for (var x = 0; x < frame.Width; x++)
            {
                cells.Add(frame.Bgra[(((y * frame.Width) + x) * 4)].ToString("D2"));
            }

            shown.Add(string.Join(',', cells));
        }

        return string.Join('\n', shown);
    }
}
