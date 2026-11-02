using System;
using System.Collections.Generic;
using System.IO;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// 系统开关与宏的启用停用各响一声，用固定音高与时长的单音做出来，不借用系统提示音。两类各有一个
/// 勾选：要安静就把那一类关掉，另一类不受影响。
/// </summary>
public class SoundSettingsTests
{
    /// <summary>
    /// 四种时刻的音两两不同，而且程序那一对与单条宏那一对在音高和长短上都分开：不看屏幕时，
    /// 听见的是哪一对就说明是哪一类变了。
    /// </summary>
    [Fact]
    public void The_two_pairs_of_state_tones_do_not_share_a_tone()
    {
        var systemOn = UiSounds.Tone(UiSound.SystemOn);
        var systemOff = UiSounds.Tone(UiSound.SystemOff);
        var macroOn = UiSounds.Tone(UiSound.MacroOn);
        var macroOff = UiSounds.Tone(UiSound.MacroOff);

        Assert.NotEqual(systemOn, systemOff);
        Assert.NotEqual(macroOn, macroOff);
        Assert.DoesNotContain(systemOn, new[] { macroOn, macroOff });
        Assert.DoesNotContain(systemOff, new[] { macroOn, macroOff });

        // 程序那一对更长，宏那一对更短；宏那一对的音高也更高。
        Assert.True(macroOn.Frequency > systemOn.Frequency);
        Assert.True(macroOff.Frequency > systemOff.Frequency);
        Assert.True(macroOn.Milliseconds < systemOn.Milliseconds);
    }

    /// <summary>默认两类都响；关掉哪一类，哪一类就不响，另一类照旧。</summary>
    [Fact]
    public void One_pair_can_be_silenced_without_the_other()
    {
        InOwnFile(_ =>
        {
            var played = new List<(int Frequency, int Milliseconds)>();
            var was = AppSound.Beep;
            try
            {
                AppSound.Beep = (frequency, milliseconds) => played.Add((frequency, milliseconds));

                Assert.True(LocalSettings.LoadSoundSystem());
                Assert.True(LocalSettings.LoadSoundMacro());

                AppSound.Play(UiSound.SystemOn);
                Assert.Equal([UiSounds.Tone(UiSound.SystemOn)], played);

                LocalSettings.StoreSoundMacro(false);
                AppSound.Play(UiSound.MacroOff);
                Assert.Single(played);

                LocalSettings.StoreSoundSystem(false);
                AppSound.Play(UiSound.SystemOff);
                Assert.Single(played);

                LocalSettings.StoreSoundMacro(true);
                AppSound.Play(UiSound.MacroOn);
                Assert.Equal(UiSounds.Tone(UiSound.MacroOn), played[^1]);

                // 设置窗口里那两个勾也是同一条路。
                var settings = new SettingsViewModel(() => DriverInputState.Ready);
                settings.SoundSystem = true;
                Assert.True(LocalSettings.LoadSoundSystem());
                settings.SoundMacro = false;
                Assert.False(LocalSettings.LoadSoundMacro());
            }
            finally
            {
                AppSound.Beep = was;
            }
        });
    }

    /// <summary>
    /// 放不出声音的机器（没有扬声器，或者音频服务没起来）只是听不到：方法返回，不抛，也不影响
    /// 点卡片或按热键那一下本身。
    /// </summary>
    [Fact]
    public void A_machine_that_cannot_make_a_tone_says_nothing()
    {
        InOwnFile(_ =>
        {
            var was = AppSound.Beep;
            try
            {
                AppSound.Beep = (_, _) => throw new InvalidOperationException("no speaker");

                AppSound.Play(UiSound.SystemOff);
            }
            finally
            {
                AppSound.Beep = was;
            }
        });
    }

    /// <summary>
    /// 交给系统的是一段完整的 16 位单声道 WAV：头里写的采样率、声道、数据长度必须与后面的字节对得上，
    /// 否则播放器要么不出声，要么只放半声。首尾都从零起，所以不会"咔"一下。
    /// </summary>
    [Fact]
    public void A_tone_is_a_complete_wave_file()
    {
        var wave = AppSound.Wave(880, 120);

        Assert.Equal("RIFF", Text(wave, 0));
        Assert.Equal("WAVE", Text(wave, 8));
        Assert.Equal("fmt ", Text(wave, 12));
        Assert.Equal("data", Text(wave, 36));

        Assert.Equal(1, BitConverter.ToInt16(wave, 20));      // PCM
        Assert.Equal(1, BitConverter.ToInt16(wave, 22));      // mono
        Assert.Equal(44100, BitConverter.ToInt32(wave, 24));
        Assert.Equal(16, BitConverter.ToInt16(wave, 34));

        // 120 毫秒、44100 赫兹、16 位单声道。
        var data = BitConverter.ToInt32(wave, 40);
        Assert.Equal(wave.Length - 44, data);
        Assert.Equal(44100 * 120 / 1000 * 2, data);
        Assert.Equal(wave.Length - 8, BitConverter.ToInt32(wave, 4));

        // 有声音，但不到满幅；两端是零。
        var peak = 0;
        for (var at = 44; at + 1 < wave.Length; at += 2)
        {
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(wave, at)));
        }

        Assert.InRange(peak, 6000, 32000);
        Assert.Equal(0, BitConverter.ToInt16(wave, 44));
        Assert.Equal(0, BitConverter.ToInt16(wave, wave.Length - 2));
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
