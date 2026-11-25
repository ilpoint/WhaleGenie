using System;
using System.IO;
using WhaleGenie.Localization;
using Xunit;

namespace WhaleGenie.Tests;

/// <summary>
/// The settings file as an object: two instances must not read each other's choices, which is what
/// lets a check or a second program run against a file of its own.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void Two_instances_keep_their_own_file_and_their_own_cache()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-appsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = new AppSettings(Path.Combine(folder, "first.json"));
            var second = new AppSettings(Path.Combine(folder, "second.json"));

            first.StoreSoundSystem(false);
            second.StoreSoundSystem(true);
            first.StoreLogCount(12);

            Assert.False(first.LoadSoundSystem());
            Assert.True(second.LoadSoundSystem());
            Assert.Equal(12, first.LoadLogCount());
            Assert.NotEqual(12, second.LoadLogCount());

            // What one instance wrote is what a fresh instance over the same file reads back.
            Assert.False(new AppSettings(Path.Combine(folder, "first.json")).LoadSoundSystem());
            Assert.True(new AppSettings(Path.Combine(folder, "second.json")).LoadSoundSystem());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_automatic_save_switch_is_on_by_default_and_round_trips()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-appsettings-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "settings.json");
            var settings = new AppSettings(file);
            Assert.True(settings.LoadAutoSaveProject());

            settings.StoreAutoSaveProject(false);
            Assert.False(new AppSettings(file).LoadAutoSaveProject());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void The_menu_pin_is_off_by_default_and_round_trips_when_remembered()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-appsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var file = Path.Combine(folder, "settings.json");
            var settings = new AppSettings(file);
            Assert.False(settings.LoadRememberMenuPin());
            Assert.False(settings.LoadMenuPinned());

            settings.StoreRememberMenuPin(true);
            settings.StoreMenuPinned(true);

            var fresh = new AppSettings(file);
            Assert.True(fresh.LoadRememberMenuPin());
            Assert.True(fresh.LoadMenuPinned());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Moving_an_instance_to_another_file_drops_what_it_cached()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-appsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var one = Path.Combine(folder, "one.json");
            var two = Path.Combine(folder, "two.json");
            File.WriteAllText(one, """{"SoundSystem":false}""");
            File.WriteAllText(two, """{"SoundSystem":true}""");

            var settings = new AppSettings(one);
            Assert.False(settings.LoadSoundSystem());

            settings.FilePath = two;
            Assert.True(settings.LoadSoundSystem());

            settings.FilePath = one;
            Assert.False(settings.LoadSoundSystem());
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
