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
