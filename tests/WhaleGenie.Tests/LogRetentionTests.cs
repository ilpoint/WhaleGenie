using System;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// 日志目录只留最近的若干份。一次运行一个失败日志、一天一个 MCP 日志，攒上一年几千个文件，
/// 而人会去翻的只有最近那几个。清理只认这两种名字：`runtime.log` 有自己的滚动，别人放进来的东西
/// 也不该被一个"清理"删掉。
/// </summary>
public class LogRetentionTests
{
    [Fact]
    public void Only_the_newest_run_logs_are_kept()
    {
        InOwnFolder(folder =>
        {
            Write(folder, "failure-old.log", -5);
            Write(folder, "failure-new.log", -1);
            Write(folder, "mcp-2026-01-01.log", -3);
            Write(folder, "runtime.log", -10);
            Write(folder, "notes.txt", -10);

            // 三份运行日志留两份：删掉的是最旧的那份失败日志。
            Assert.Equal(1, LogRetention.Prune(folder, 2));

            Assert.True(File.Exists(Path.Combine(folder, "mcp-2026-01-01.log")));
            Assert.True(File.Exists(Path.Combine(folder, "failure-new.log")));
            Assert.False(File.Exists(Path.Combine(folder, "failure-old.log")));

            // 自管的滚动日志与不是这个程序写的文件都留着。
            Assert.True(File.Exists(Path.Combine(folder, "runtime.log")));
            Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
        });
    }

    /// <summary>少于要留的份数时什么也不删；份数本身也被收在范围内，0 指的是"只留一份"。</summary>
    [Fact]
    public void A_folder_that_fits_is_left_alone()
    {
        InOwnFolder(folder =>
        {
            Write(folder, "failure-one.log", -2);
            Write(folder, "mcp-2026-01-02.log", -1);

            Assert.Equal(0, LogRetention.Prune(folder, LogRetention.Usual));
            Assert.Equal(0, LogRetention.Prune(NoSuchFolder(), LogRetention.Usual));

            Assert.Equal(1, LogRetention.Prune(folder, 0));
            Assert.Single(Directory.GetFiles(folder, "mcp-*.log"));
            Assert.Empty(Directory.GetFiles(folder, "failure-*.log"));
        });

        static string NoSuchFolder()
            => Path.Combine(Path.GetTempPath(), "whalegenie-no-such-folder-" + Guid.NewGuid().ToString("N"));
    }

    /// <summary>默认留 100 份；写进去的数被收在 1～999 之间。</summary>
    [Fact]
    public void The_count_defaults_to_a_hundred_and_stays_in_range()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-logs-" + Guid.NewGuid().ToString("N"));
        var stored = LocalSettings.FilePath;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");

            Assert.Equal(100, LocalSettings.LoadLogCount());

            LocalSettings.StoreLogCount(20);
            Assert.Equal(20, LocalSettings.LoadLogCount());

            LocalSettings.StoreLogCount(0);
            Assert.Equal(LogRetention.Fewest, LocalSettings.LoadLogCount());

            LocalSettings.StoreLogCount(5000);
            Assert.Equal(LogRetention.Most, LocalSettings.LoadLogCount());

            // 设置窗口里的数字框也是同一条路。
            var settings = new SettingsViewModel(() => DriverInputState.Ready);
            settings.LogCount = 30;
            Assert.Equal(30, LocalSettings.LoadLogCount());
        }
        finally
        {
            LocalSettings.FilePath = stored;
            Directory.Delete(folder, true);
        }
    }

    /// <summary>Writes a file with the age the check needs, since the rule is about modification time.</summary>
    private static void Write(string folder, string name, int days)
    {
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, name);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(days));
    }

    private static void InOwnFolder(Action<string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-logs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            check(folder);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
