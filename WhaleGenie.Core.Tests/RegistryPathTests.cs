using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// 注册表路径要写成什么样子，是「默认只碰当前用户」这句话能不能站住的地方：路径里带上根名，
/// 上一格选的根就成了摆设。判定是一段不碰注册表的纯函数，可以一条一条量。
/// </summary>
public class RegistryPathTests
{
    [Theory]
    [InlineData("Software")]
    [InlineData("Software\\Vendor\\App")]
    [InlineData("Software\\Vendor\\App v2")]
    public void A_path_below_a_root_is_taken_as_written(string written)
        => Assert.Equal(written, WindowsSystemDevice.RegistryPath(written));

    /// <summary>两端的空白是人的手误，不是路径的一部分。</summary>
    [Fact]
    public void Space_around_a_path_is_not_part_of_it()
        => Assert.Equal(@"Software\App", WindowsSystemDevice.RegistryPath("  Software\\App  "));

    [Theory]
    [InlineData("\\Software")]
    [InlineData("Software\\")]
    [InlineData("Software\\\\App")]
    [InlineData("Software/App")]
    [InlineData("Software:App")]
    [InlineData("Software\\..\\Other")]
    [InlineData("Software\\.\\App")]
    [InlineData("HKEY_CURRENT_USER\\Software")]
    [InlineData("HKCU\\Software")]
    [InlineData("HKLM\\Software")]
    public void A_path_that_names_the_root_or_climbs_is_refused(string written)
    {
        var refused = Assert.Throws<DeviceActionException>(
            () => WindowsSystemDevice.RegistryPath(written));

        Assert.Equal(RunKeys.BadRegistryName, refused.Key);
    }

    /// <summary>空着不走「名字不对」那条：这一格根本没答。</summary>
    [Fact]
    public void An_empty_path_says_it_is_missing()
    {
        var refused = Assert.Throws<DeviceActionException>(() => WindowsSystemDevice.RegistryPath(" "));

        Assert.Equal(RunKeys.MissingPath, refused.Key);
    }
}
