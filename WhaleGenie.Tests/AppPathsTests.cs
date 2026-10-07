using System;
using System.IO;
using WhaleGenie.Execution;

namespace WhaleGenie.Tests;

/// <summary>
/// What the running copy of WhaleGenie keeps beside itself. A portable copy has to be able to carry
/// its choices and its failure pictures, so both names are checked here rather than assumed.
/// </summary>
public class AppPathsTests
{
    [Fact]
    public void The_settings_and_the_pictures_sit_beside_the_program()
    {
        Assert.Equal(Path.Combine(AppPaths.Root, "settings.json"), AppPaths.Settings);
        Assert.Equal(Path.Combine(AppPaths.Root, "logs"), AppPaths.Logs);
    }
}
