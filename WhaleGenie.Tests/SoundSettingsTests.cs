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
/// 系统开关与宏的启用停用各响一声。这两对默认就不是同一种声音：盲按热键换整个程序的状态时，
/// 听见的是"程序的那一对"；在列表里点一条宏时，听见的是"一条宏的那一对"。
/// </summary>
public class SoundSettingsTests
{
    /// <summary>
    /// 四种时刻的默认声音两两不同 —— 这是这条设置存在的理由，所以单独立一条：改默认值的人会先看到它。
    /// </summary>
    [Fact]
    public void The_two_pairs_of_state_sounds_do_not_share_a_sound()
    {
        var systemOn = UiSounds.Default(UiSound.SystemOn);
        var systemOff = UiSounds.Default(UiSound.SystemOff);
        var macroOn = UiSounds.Default(UiSound.MacroOn);
        var macroOff = UiSounds.Default(UiSound.MacroOff);

        Assert.NotEqual(systemOn, systemOff);
        Assert.NotEqual(macroOn, macroOff);
        Assert.DoesNotContain(systemOn, new[] { macroOn, macroOff });
        Assert.DoesNotContain(systemOff, new[] { macroOn, macroOff });
    }

    /// <summary>默认开启；改一种时刻只改那一种，其余三个留在各自的值上。</summary>
    [Fact]
    public void One_chosen_sound_leaves_the_other_three_alone()
    {
        InOwnFile(_ =>
        {
            Assert.True(LocalSettings.LoadSounds());
            Assert.Equal(SoundKind.Information, LocalSettings.LoadSound(UiSound.SystemOn));
            Assert.Equal(SoundKind.Warning, LocalSettings.LoadSound(UiSound.SystemOff));
            Assert.Equal(SoundKind.Default, LocalSettings.LoadSound(UiSound.MacroOn));
            Assert.Equal(SoundKind.Question, LocalSettings.LoadSound(UiSound.MacroOff));

            LocalSettings.StoreSound(UiSound.MacroOn, SoundKind.Error);
            Assert.Equal(SoundKind.Error, LocalSettings.LoadSound(UiSound.MacroOn));
            Assert.Equal(SoundKind.Information, LocalSettings.LoadSound(UiSound.SystemOn));

            // 设置窗口改的值也是同一条路：选中即写回。
            var settings = new SettingsViewModel(() => DriverInputState.Ready);
            settings.SystemOffSound = settings.SoundChoices.First(option => option.Kind == SoundKind.Question);
            Assert.Equal(SoundKind.Question, LocalSettings.LoadSound(UiSound.SystemOff));

            settings.Sounds = false;
            Assert.False(LocalSettings.LoadSounds());
        });
    }

    /// <summary>
    /// 关掉之后一声都不发；开着时发的就是那一格挑的声音。没有声卡的机器拒绝播放，那一声听不到，
    /// 但开关本身不能因此出错。
    /// </summary>
    [Fact]
    public void A_sound_is_played_only_while_it_is_asked_for()
    {
        InOwnFile(_ =>
        {
            var system = new SilentSystem();

            AppSound.Play(system, UiSound.SystemOn);
            Assert.Equal([SoundKind.Information], system.Played);

            LocalSettings.StoreSounds(false);
            AppSound.Play(system, UiSound.SystemOff);
            Assert.Single(system.Played);

            LocalSettings.StoreSounds(true);
            LocalSettings.StoreSound(UiSound.MacroOff, SoundKind.Error);
            AppSound.Play(system, UiSound.MacroOff);
            Assert.Equal(SoundKind.Error, system.Played[^1]);

            // 放不出声音的机器：这里只要求它不抛，调用方是点卡片的那一下。
            system.Refuses = true;
            AppSound.Play(system, UiSound.MacroOn);
            Assert.Equal(2, system.Played.Count);
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

    /// <summary>A machine that writes down what it was asked to play, or refuses to play at all.</summary>
    private sealed class SilentSystem : ISystemDevice
    {
        public List<SoundKind> Played { get; } = [];

        public bool Refuses { get; set; }

        public void PlaySound(SoundKind kind)
        {
            if (Refuses)
            {
                throw new DeviceActionException("Run.SoundRefused");
            }

            Played.Add(kind);
        }

        public string Info(string field) => string.Empty;

        public string Environment(string name) => string.Empty;

        public void Power(PowerAction action, int graceSeconds)
        {
        }

        public int Volume() => 50;

        public void SetVolume(int percent)
        {
        }

        public bool IsMuted() => false;

        public void SetMuted(bool muted)
        {
        }

        public void Notify(string title, string text, NotificationKind kind)
        {
        }

        public string InputMethod() => string.Empty;

        public IReadOnlyList<string> InputMethods() => [];

        public string? SwitchInputMethod(string layout) => null;

        public int Brightness() => 50;

        public void SetBrightness(int percent)
        {
        }
    }
}
