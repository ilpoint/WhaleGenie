using Viktor.Execution;
using Viktor.Models;

namespace Viktor.Tests;

/// <summary>
/// Watching a file or a folder. These tests write real files in a folder of their own under the
/// temporary folder, because the thing under test is the file system's own notifications.
/// </summary>
public class FileWatchTests
{
    /// <summary>How long a test waits for the file system to say something happened.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void Changing_what_is_watched_asks_for_a_watch_of_its_own()
    {
        var macro = new MacroItem { TriggerMode = MacroTrigger.FileChanges, WatchPath = "notes.txt" };
        var first = FileWatch.SignatureOf(macro, "/tmp/notes.txt");

        Assert.Equal(first, FileWatch.SignatureOf(macro, "/tmp/notes.txt"));

        macro.WatchFilter = "*.log";
        Assert.NotEqual(first, FileWatch.SignatureOf(macro, "/tmp/notes.txt"));

        macro.WatchFilter = "*";
        macro.WatchChange = FileChangeKind.Created;
        Assert.NotEqual(first, FileWatch.SignatureOf(macro, "/tmp/notes.txt"));

        macro.WatchChange = FileChangeKind.Any;
        macro.WatchSubfolders = true;
        Assert.NotEqual(first, FileWatch.SignatureOf(macro, "/tmp/notes.txt"));

        Assert.NotEqual(first, FileWatch.SignatureOf(macro, "/tmp/other.txt"));
    }

    [Fact]
    public void Writing_to_a_watched_file_is_one_change_however_many_events_it_raises()
    {
        WithFolder(folder =>
        {
            var file = Path.Combine(folder, "notes.txt");
            File.WriteAllText(file, "one");

            using var watch = WatchAnything(file);
            Wait(() => watch.Take(DateTime.Now));

            // The events of that write are done with: a second look finds nothing new to report.
            Assert.False(watch.Take(DateTime.Now));

            File.AppendAllText(file, "two");
            Assert.True(Wait(() => watch.Take(DateTime.Now)));
        });
    }

    [Fact]
    public void A_watch_only_reports_the_kind_of_change_it_was_asked_about()
    {
        WithFolder(folder =>
        {
            var file = Path.Combine(folder, "notes.txt");
            File.WriteAllText(file, "one");

            using var watch = FileWatch.Create(
                new MacroItem
                {
                    TriggerMode = MacroTrigger.FileChanges,
                    WatchPath = file,
                    WatchChange = FileChangeKind.Deleted,
                },
                file);

            watch.Ensure(DateTime.Now);

            // Writing to the file is a change, which this watch does not ask about, so nothing is
            // reported however long it is waited for.
            File.AppendAllText(file, "two");
            Thread.Sleep(FileWatch.QuietPeriod + FileWatch.QuietPeriod);
            Assert.False(watch.Take(DateTime.Now));

            File.Delete(file);
            Assert.True(Wait(() => watch.Take(DateTime.Now)));
        });
    }

    [Fact]
    public void A_watched_folder_reports_the_files_made_inside_it()
    {
        WithFolder(folder =>
        {
            using var watch = FileWatch.Create(
                new MacroItem
                {
                    TriggerMode = MacroTrigger.FileChanges,
                    WatchPath = folder,
                    WatchFilter = "*.log",
                    WatchChange = FileChangeKind.Created,
                },
                folder);

            watch.Ensure(DateTime.Now);

            File.WriteAllText(Path.Combine(folder, "notes.txt"), "not asked about");
            Thread.Sleep(FileWatch.QuietPeriod + FileWatch.QuietPeriod);
            Assert.False(watch.Take(DateTime.Now));

            File.WriteAllText(Path.Combine(folder, "run.log"), "asked about");
            Assert.True(Wait(() => watch.Take(DateTime.Now)));
        });
    }

    [Fact]
    public void A_path_that_is_not_there_yet_is_left_for_later_rather_than_crashing()
    {
        using var watch = FileWatch.Create(
            new MacroItem { TriggerMode = MacroTrigger.FileChanges, WatchPath = "missing.txt" },
            Path.Combine(Path.GetTempPath(), "viktor-not-here-" + Guid.NewGuid(), "missing.txt"));

        watch.Ensure(DateTime.Now);

        Assert.False(watch.Take(DateTime.Now));
    }

    private static FileWatch WatchAnything(string path)
    {
        var watch = FileWatch.Create(
            new MacroItem { TriggerMode = MacroTrigger.FileChanges, WatchPath = path },
            path);

        watch.Ensure(DateTime.Now);
        return watch;
    }

    /// <summary>
    /// Takes the change if it has come, looking again the way the trigger service's timer does,
    /// because the file system reports a change on a thread of its own.
    /// </summary>
    private static bool Wait(Func<bool> take)
    {
        var end = DateTime.Now + Patience;
        while (DateTime.Now < end)
        {
            if (take())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    /// <summary>Runs a test in a folder of its own and clears it away afterwards.</summary>
    private static void WithFolder(Action<string> body)
    {
        var folder = Path.Combine(Path.GetTempPath(), "viktor-watch-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            body(folder);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
