using System;
using System.IO;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// Where the running copy of WhaleGenie keeps what belongs to the person using it. These live in
/// their profile rather than beside the program, because an update or a reinstall replaces the
/// program's folder and would take the settings, the log and the unsaved work with it.
/// </summary>
public class AppPathsTests
{
    [Fact]
    public void The_settings_and_the_log_live_in_the_persons_own_folder()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhaleGenie");

        Assert.Equal(expected, AppPaths.State);
        Assert.Equal(Path.Combine(expected, "settings.json"), AppPaths.Settings);
        Assert.Equal(Path.Combine(expected, "logs"), AppPaths.Logs);
        Assert.Equal(Path.Combine(expected, "recovery.json"), AppPaths.Recovery);

        // 三样都在同一个文件夹里：找一份要发给别人的东西时，不需要在两处翻。
        Assert.StartsWith(AppPaths.State, AppPaths.Settings, StringComparison.Ordinal);
        Assert.StartsWith(AppPaths.State, AppPaths.Logs, StringComparison.Ordinal);
    }

    /// <summary>
    /// 旧版本把设置存在程序目录旁。第一次用新版本时那份文件被搬过来，而不是被忽略、让用户重新
    /// 设一遍；搬走以后旧位置不再留下副本，免得两份各说各话。
    /// </summary>
    [Fact]
    public void A_settings_file_beside_the_program_is_moved_into_the_profile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-adopt-" + Guid.NewGuid().ToString("N"));
        var program = Path.Combine(folder, "program");
        var profile = Path.Combine(folder, "profile");
        Directory.CreateDirectory(program);

        try
        {
            var beside = Path.Combine(program, "settings.json");
            File.WriteAllText(beside, "{\"SystemAtStart\":true}");

            var adopted = LocalSettings.Adopt(Path.Combine(profile, "settings.json"), program);

            Assert.Equal(Path.Combine(profile, "settings.json"), adopted);
            Assert.True(File.Exists(adopted));
            Assert.Contains("SystemAtStart", File.ReadAllText(adopted));
            Assert.False(File.Exists(beside));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    /// <summary>新位置已经有文件时，程序旁那份不再搬进来覆盖它。</summary>
    [Fact]
    public void A_file_already_in_the_profile_is_left_alone()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-adopt-" + Guid.NewGuid().ToString("N"));
        var program = Path.Combine(folder, "program");
        var profile = Path.Combine(folder, "profile");
        Directory.CreateDirectory(program);
        Directory.CreateDirectory(profile);

        try
        {
            File.WriteAllText(Path.Combine(program, "settings.json"), "{\"Old\":true}");
            var wanted = Path.Combine(profile, "settings.json");
            File.WriteAllText(wanted, "{\"New\":true}");

            var adopted = LocalSettings.Adopt(wanted, program);

            Assert.Equal(wanted, adopted);
            Assert.Contains("New", File.ReadAllText(wanted));
            Assert.True(File.Exists(Path.Combine(program, "settings.json")));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    /// <summary>
    /// 程序自己的文件夹只留在那儿的东西：公共宏库跟着它，因为那是可以被拷走的成品。
    /// </summary>
    [Fact]
    public void The_public_library_stays_with_the_program()
    {
        Assert.StartsWith(AppPaths.Root, AppPaths.PublicMacroLibrary, StringComparison.Ordinal);
    }
}
