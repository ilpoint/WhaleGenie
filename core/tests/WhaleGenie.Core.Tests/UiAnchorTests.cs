using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// 锚点说的是「这个元素在那一元素的哪一边」，所以判定只看一条轴：同一行里靠右的两个按钮，
/// 上下差几个像素仍旧是「在右边」。这套判定不碰 UIA，可以一条一条量。
/// </summary>
public class UiAnchorTests
{
    private static UiAnchor At(UiSide side, int gap = 0)
        => new(UiQuery.Parse("Button[name='确定']"), side, gap);

    private static ScreenRect Box(int left, int top, int width = 40, int height = 20)
        => new(left, top, width, height);

    [Theory]
    [InlineData(UiSide.Right, 100, 0, true)]
    [InlineData(UiSide.Right, 100, 60, true)]
    [InlineData(UiSide.Right, 100, 61, false)]
    [InlineData(UiSide.Right, 20, 0, false)]
    [InlineData(UiSide.Left, -40, 0, true)]
    [InlineData(UiSide.Left, -39, 0, false)]
    public void An_element_is_kept_only_on_the_side_asked_for(UiSide side, int left, int gap,
        bool kept)
    {
        var anchor = At(side, gap);
        var candidate = Box(left, 100);

        Assert.Equal(kept, anchor.Holds(candidate, Box(0, 100)));
    }

    [Theory]
    [InlineData(UiSide.Above, 0, -30, true)]
    [InlineData(UiSide.Above, 0, 0, false)]
    [InlineData(UiSide.Below, 0, 20, true)]
    [InlineData(UiSide.Below, 0, 10, false)]
    public void The_same_rule_runs_down_the_screen(UiSide side, int left, int top, bool kept)
    {
        var anchor = At(side);
        var candidate = Box(left, top);

        Assert.Equal(kept, anchor.Holds(candidate, Box(0, 0)));
    }

    /// <summary>
    /// 压在一起的两个元素没有「在哪一边」：左边要求它的右边缘不越过锚点左边缘，一重叠就都不成立。
    /// 这是「在左边」，不是「偏左一点」。
    /// </summary>
    [Fact]
    public void An_element_overlapping_its_anchor_is_on_no_side()
    {
        var candidate = Box(20, 0);
        var anchor = Box(0, 0);

        foreach (var side in new[] { UiSide.Left, UiSide.Right, UiSide.Above, UiSide.Below })
        {
            Assert.False(At(side).Holds(candidate, anchor), $"{side}");
        }
    }

    /// <summary>
    /// 提供方说不出元素在哪儿时，答案是「哪一边都不算」，不是「通过」：把不知道当成通过，
    /// 两个同名按钮里就会挑到没有位置的那一个。
    /// </summary>
    [Fact]
    public void A_rectangle_nobody_could_measure_matches_nothing()
    {
        var nowhere = new ScreenRect(0, 0, 0, 0);

        Assert.False(At(UiSide.Right).Holds(Box(200, 0), nowhere));
        Assert.False(At(UiSide.Right).Holds(nowhere, Box(0, 0)));
    }

    /// <summary>零像素就是贴着边也算；负数按零算，目录里最小值本来也是 0。</summary>
    [Fact]
    public void Touching_the_anchor_counts_and_a_negative_gap_is_read_as_none()
    {
        var touching = Box(40, 0);

        Assert.True(At(UiSide.Right, 0).Holds(touching, Box(0, 0)));
        Assert.True(At(UiSide.Right, -50).Holds(touching, Box(0, 0)));
    }
}
