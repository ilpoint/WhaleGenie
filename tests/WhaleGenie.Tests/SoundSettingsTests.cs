using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// 系统开关与宏的启用停用各响一声：程序那一对与宏那一对用的是不同的音效，不看屏幕也分得清。
/// 两类各有一个勾选，关掉一类不影响另一类。
/// </summary>
public class SoundSettingsTests
{
    /// <summary>
    /// 四个时刻各有一个自己的音效文件，而且都真的打得开：资源少了、名字写错了，或放进去的不是
    /// WAV，声音就会变成静默，而那正是这条设置存在的理由。
    /// </summary>
    [Fact]
    public void Every_moment_has_a_sound_of_its_own()
    {
        var waves = new Dictionary<UiSound, byte[]>();
        foreach (var which in Enum.GetValues<UiSound>())
        {
            var wave = AppSound.Wave(which);
            Assert.Equal("RIFF", Text(wave, 0));
            Assert.Equal("WAVE", Text(wave, 8));

            // 头部得是 16 位单声道的 PCM，而且后面真有一段数据；否则播放器只会静默。
            var format = Chunk(wave, "fmt ");
            Assert.NotNull(format);
            Assert.Equal(1, BitConverter.ToInt16(wave, format!.Value.At));
            Assert.Equal(16, BitConverter.ToInt16(wave, format.Value.At + 14));

            var data = Chunk(wave, "data");
            Assert.NotNull(data);
            Assert.True(data!.Value.Size > 8000, $"{which} is too short to hear");

            waves[which] = wave;
        }

        // 两两不同：系统启用与停用不是同一个音，程序那一对与宏那一对也不是。
        var distinct = waves.Values.Select(wave => Convert.ToBase64String(wave)).Distinct().Count();
        Assert.Equal(4, distinct);
    }

    /// <summary>The body of one chunk: where its bytes start and how many there are.</summary>
    private static (int At, int Size)? Chunk(byte[] wave, string id)
    {
        for (var at = 12; at + 8 <= wave.Length;)
        {
            var size = BitConverter.ToInt32(wave, at + 4);
            if (Text(wave, at) == id)
            {
                return (at + 8, size);
            }

            at += 8 + size + (size & 1);
        }

        return null;
    }

    /// <summary>默认两类都响；关掉哪一类，哪一类就不响，另一类照旧。</summary>
    [Fact]
    public void One_pair_can_be_silenced_without_the_other()
    {
        InOwnFile(_ =>
        {
            var played = new List<UiSound>();
            var was = AppSound.PlayTone;
            try
            {
                AppSound.PlayTone = moment => played.Add(moment);

                Assert.True(LocalSettings.LoadSoundSystem());
                Assert.True(LocalSettings.LoadSoundMacro());

                AppSound.Play(UiSound.SystemOn);
                Assert.Equal([UiSound.SystemOn], played);

                LocalSettings.StoreSoundMacro(false);
                AppSound.Play(UiSound.MacroOff);
                Assert.Single(played);

                LocalSettings.StoreSoundSystem(false);
                AppSound.Play(UiSound.SystemOff);
                Assert.Single(played);

                LocalSettings.StoreSoundMacro(true);
                AppSound.Play(UiSound.MacroOn);
                Assert.Equal(UiSound.MacroOn, played[^1]);

                // 设置窗口里那两个勾也是同一条路。
                var settings = new SettingsViewModel(() => DriverInputState.Ready);
                settings.SoundSystem = true;
                Assert.True(LocalSettings.LoadSoundSystem());
                settings.SoundMacro = false;
                Assert.False(LocalSettings.LoadSoundMacro());
            }
            finally
            {
                AppSound.PlayTone = was;
            }
        });
    }

    /// <summary>
    /// 放不出声音的机器（没有输出设备，或者音频服务没起来）只是听不到：方法返回，不抛，也不影响
    /// 点卡片或按热键那一下本身。
    /// </summary>
    [Fact]
    public void A_machine_that_cannot_play_a_sound_says_nothing()
    {
        InOwnFile(_ =>
        {
            var was = AppSound.PlayTone;
            try
            {
                AppSound.PlayTone = _ => throw new InvalidOperationException("no output device");

                AppSound.Play(UiSound.SystemOff);
            }
            finally
            {
                AppSound.PlayTone = was;
            }
        });
    }

    /// <summary>The four characters of a chunk name, read as they sit in the file.</summary>
    private static string Text(byte[] wave, int at)
        => System.Text.Encoding.ASCII.GetString(wave, at, 4);

    /// <summary>
    /// Runs the check against a settings file of its own, then puts the real one back: the choices
    /// of whoever is using WhaleGenie are not a check's to write.
    /// </summary>
    private static void InOwnFile(Action<string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-sounds-" + Guid.NewGuid().ToString("N"));
        var stored = LocalSettings.FilePath;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            check(LocalSettings.FilePath);
        }
        finally
        {
            LocalSettings.FilePath = stored;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
