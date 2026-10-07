using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Tests;

/// <summary>
/// The window going out of the way instead of closing. A macro is set off by a key or a pixel, not
/// by the window being open, so the program keeps running with only its icon in the notification
/// area to show for it — and that icon's menu is the one deliberate way to stop.
///
/// The icon itself belongs to the shell, so the policy is read here against a stand-in area, and
/// the real one is left to a person looking at the notification area.
/// </summary>
public class TrayTests
{
    [Fact]
    public void Minimizing_puts_the_window_out_of_the_way_and_keeps_the_program_running()
    {
        Ui.Run(() =>
        {
            var window = new Window();
            window.Show();

            var area = new FakeArea();
            var exits = 0;
            var tray = new AppTray(window, area, () => exits++);

            window.WindowState = WindowState.Minimized;

            Assert.False(window.IsVisible);
            Assert.Equal(0, exits);

            // Once per session is enough: the user is told where it went the first time and does
            // not need the same sentence on every minimize.
            Assert.Single(area.Balloons);

            tray.Dispose();
            window.Close();
        });
    }

    [Fact]
    public void Closing_the_window_hides_it_rather_than_ending_the_program()
    {
        Ui.Run(() =>
        {
            var window = new Window();
            window.Show();

            var area = new FakeArea();
            var exits = 0;
            var tray = new AppTray(window, area, () => exits++);

            window.Close();

            Assert.Equal(0, exits);
            Assert.False(window.IsVisible);
            Assert.Single(area.Balloons);

            // With the icon gone the window is an ordinary window again, which is what lets the
            // test put it away.
            tray.Dispose();
            window.Close();
        });
    }

    [Fact]
    public void A_click_on_the_icon_brings_the_window_back()
    {
        Ui.Run(() =>
        {
            var window = new Window();
            window.Show();

            var area = new FakeArea();
            var tray = new AppTray(window, area, () => { });

            window.Hide();
            Assert.False(window.IsVisible);

            area.RaiseActivated();

            Assert.True(window.IsVisible);

            tray.Dispose();
            window.Close();
        });
    }

    [Fact]
    public void The_icons_menu_is_what_stops_the_program()
    {
        Ui.Run(() =>
        {
            var window = new Window();
            window.Show();

            var area = new FakeArea();
            var exits = 0;
            var tray = new AppTray(window, area, () => exits++);

            window.Hide();
            area.RaiseExitRequested();

            Assert.Equal(1, exits);

            tray.Dispose();
            window.Close();
        });
    }

    [Fact]
    public void The_balloon_says_where_the_window_went()
    {
        Ui.Run(() =>
        {
            var window = new Window();
            window.Show();

            var area = new FakeArea();
            var tray = new AppTray(window, area, () => { });

            window.WindowState = WindowState.Minimized;

            Assert.Equal(
                [$"{NotificationKind.Information}|{Strings.Get("Tray.HiddenTitle")}|{Strings.Get("Tray.HiddenText")}"],
                area.Balloons);

            tray.Dispose();
            window.Close();
        });
    }

    [Fact]
    public void What_the_icon_says_is_written_in_both_languages()
    {
        string[] keys = ["Tray.Tooltip", "Tray.Show", "Tray.Exit", "Tray.HiddenTitle", "Tray.HiddenText"];

        Assert.All(keys, key =>
        {
            Assert.True(Strings.English.ContainsKey(key), $"English {key} is missing");
            Assert.True(Strings.Chinese.ContainsKey(key), $"Chinese {key} is missing");
        });
    }

    /// <summary>
    /// Stands in for the shell's notification area: it records the balloons and lets a test be the
    /// person clicking the icon.
    /// </summary>
    private sealed class FakeArea : INotificationArea
    {
        public List<string> Balloons { get; } = [];

        public event Action? Activated;

        public event Action? ExitRequested;

        public void Balloon(string title, string text, NotificationKind kind)
            => Balloons.Add($"{kind}|{title}|{text}");

        public void RaiseActivated() => Activated?.Invoke();

        public void RaiseExitRequested() => ExitRequested?.Invoke();

        public void Dispose()
        {
        }
    }
}
