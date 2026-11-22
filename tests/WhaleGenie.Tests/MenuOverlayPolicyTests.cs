using WhaleGenie.Runtime;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>A menu closes when another program comes forward, unless it was pinned.</summary>
public class MenuOverlayPolicyTests
{
    [Theory]
    [InlineData(true, false, 10, 20, true)]
    [InlineData(true, true, 10, 20, false)]
    [InlineData(true, false, 10, 10, false)]
    [InlineData(false, false, 10, 20, false)]
    [InlineData(true, false, 10, 0, false)]
    public void It_closes_only_for_another_program_and_only_when_not_pinned(
        bool visible, bool pinned, long own, long foreground, bool expected)
        => Assert.Equal(expected, MenuOverlayPolicy.ShouldClose(visible, pinned, own, foreground));
}
