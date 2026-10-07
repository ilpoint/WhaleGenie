using Avalonia.Controls;
using Avalonia.Threading;
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

    [Fact]
    public void Leaving_waits_for_a_window_that_is_asking_about_unsaved_work()
    {
        Ui.Run(() =>
        {
            var host = new Window();
            host.Show();

            var tray = new AppTray(host, new FakeArea(), () => { });

            var stubborn = new Window();
            var asked = 0;
            var allow = false;
            stubborn.Closing += (_, e) =>
            {
                if (!allow)
                {
                    e.Cancel = true;
                    asked++;
                }
            };
            stubborn.Show();

            var plain = new Window();
            plain.Show();

            var left = 0;
            tray.Walk([plain, stubborn], () => left++);

            // The window opened last is asked first, and while it is asking nothing has gone
            // anywhere — the changes it is asking about are still there to be saved.
            Assert.Equal(1, asked);
            Assert.True(stubborn.IsVisible);
            Assert.True(plain.IsVisible);
            Assert.Equal(0, left);

            // Answering the question lets it go, and the rest of the way out runs with it.
            allow = true;
            stubborn.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, left);
            Assert.False(stubborn.IsVisible);
            Assert.False(plain.IsVisible);

            tray.Dispose();
            host.Close();
        });
    }

    [Fact]
    public void A_question_that_is_dismissed_calls_the_way_out_off()
    {
        Ui.Run(() =>
        {
            var host = new Window();
            host.Show();

            var stubborn = new Window();
            var allow = false;
            stubborn.Closing += (_, e) =>
            {
                if (!allow)
                {
                    e.Cancel = true;
                }
            };
            stubborn.Show();

            var area = new FakeArea();
            var left = 0;
            AppTray? tray = null;
            tray = new AppTray(host, area, () => tray!.Walk([stubborn], () => left++));

            area.RaiseExitRequested();

            // The way out is waiting on the question the window put up.
            Assert.True(stubborn.IsVisible);
            Assert.True(tray.IsLeaving);
            Assert.Equal(0, left);

            // The question is dismissed, so the way out it belonged to is off. Closing that window
            // later is an ordinary close: nothing may be waiting to take the program down with it.
            tray.CalledOff();

            Assert.False(tray.IsLeaving);

            allow = true;
            stubborn.Close();

            Assert.Equal(0, left);

            tray.Dispose();
            host.Close();
        });
    }

    [Fact]
    public void A_window_asking_from_the_notification_area_still_stops_the_way_out()
    {
        Ui.Run(() =>
        {
            var host = new Window();
            host.Show();

            // Away in the notification area, which is where the main window spends most of its
            // life — and where a question about unsaved work still has to be asked.
            var hidden = new Window();
            hidden.Show();
            hidden.Closing += (_, e) => e.Cancel = true;
            hidden.Hide();

            var tray = new AppTray(host, new FakeArea(), () => { });
            var left = 0;
            tray.Walk([hidden], () => left++);

            Assert.Equal(0, left);

            tray.Dispose();
            host.Close();
        });
    }

    [Fact]
    public void The_question_after_a_window_closed_sees_what_that_window_handed_over()
    {
        Ui.Run(() =>
        {
            var host = new Window();
            host.Show();
            var tray = new AppTray(host, new FakeArea(), () => { });

            // The window asked first is one that hands something over on its way out — the macro
            // editor giving its macro to the list — and the hand-over takes a turn to arrive, the
            // way the result of a dialog does.
            var handedOver = false;
            var editor = new Window();
            var writing = true;
            editor.Closing += (_, e) =>
            {
                if (writing)
                {
                    e.Cancel = true;
                }
                else
                {
                    Dispatcher.UIThread.Post(() => handedOver = true);
                }
            };
            editor.Show();

            // The window asked next is the one that has to know whether it arrived.
            var landed = false;
            var project = new Window();
            var asking = true;
            project.Closing += (_, e) =>
            {
                if (asking)
                {
                    e.Cancel = true;
                    landed = handedOver;
                }
            };
            project.Show();

            var left = 0;
            tray.Walk([project, editor], () => left++);

            writing = false;
            editor.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.True(handedOver);
            Assert.True(landed);
            Assert.Equal(0, left);

            asking = false;
            project.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, left);

            tray.Dispose();
            host.Close();
        });
    }

    [Fact]
    public void The_icons_menu_closes_the_window_rather_than_hiding_it()
    {
        Ui.Run(() =>
        {
            var window = new Window();
            window.Show();

            var area = new FakeArea();
            var left = 0;
            AppTray? tray = null;
            tray = new AppTray(window, area, () => tray!.Walk([window], () => left++));

            area.RaiseExitRequested();

            // This is the one close that really leaves: the window goes, and the program with it.
            Assert.Equal(1, left);
            Assert.False(window.IsVisible);
            Assert.True(tray!.IsLeaving);

            tray.Dispose();
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
