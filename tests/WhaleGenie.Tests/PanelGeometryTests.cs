using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>The panel's size and its place inside the target's client area.</summary>
public class PanelGeometryTests
{
    [Fact]
    public void A_vertical_panel_grows_one_row_per_member_up_to_the_limit()
    {
        var one = PanelGeometry.Size(1, horizontal: false, collapsed: false);
        var many = PanelGeometry.Size(20, horizontal: false, collapsed: false);

        Assert.Equal(86, one.Width);
        // The card's border and member padding are part of the window, or its rounded right and
        // bottom corners would be cut off.
        Assert.Equal(20 + 6 + 24, one.Height);
        Assert.Equal(20 + 6 + 6 * 24 + 5 * 2, many.Height);
    }

    [Fact]
    public void A_horizontal_panel_is_as_wide_as_its_visible_members()
    {
        var size = PanelGeometry.Size(3, horizontal: true, collapsed: false);

        Assert.Equal(3 * 80 + 2 * 2 + 6, size.Width);
        Assert.Equal(20 + 24 + 6, size.Height);
    }

    [Fact]
    public void Placement_is_clamped_inside_the_client_area()
    {
        var (x, y) = PanelGeometry.Place((100, 50), (400, 300), offsetX: 999, offsetY: -20,
            panel: (86, 100));

        Assert.Equal(100 + (400 - 86), x);
        Assert.Equal(50, y);
    }

    /// <summary>
    /// The offset a drag stores is the same clamp, without the target's origin added back. A panel
    /// released past the edge comes to rest at the edge, and the next frame is drawn there too.
    /// </summary>
    [Fact]
    public void A_dragged_offset_is_held_inside_the_client_area()
    {
        var (x, y) = PanelGeometry.ClampOffset((400, 300), offsetX: 999, offsetY: -20,
            panel: (86, 100));

        Assert.Equal(400 - 86, x);
        Assert.Equal(0, y);
    }
}
