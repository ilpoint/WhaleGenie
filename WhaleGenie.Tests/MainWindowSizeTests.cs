using Avalonia.Threading;
using WhaleGenie.Localization;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MainWindowSizeTests
{
    [Fact]
    public void The_main_window_reopens_at_the_size_it_was_closed_at()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-window-" + Guid.NewGuid().ToString("N"));
        var stored = LocalSettings.FilePath;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");

            Ui.Run(() =>
            {
                var first = new MainWindow
                {
                    Width = 620,
                    Height = 740,
                };
                first.Show();
                Dispatcher.UIThread.RunJobs();
                first.Width = 620;
                first.Height = 740;
                Dispatcher.UIThread.RunJobs();
                first.Close();

                var second = new MainWindow();
                second.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(620, second.Width);
                Assert.Equal(740, second.Height);
                second.Close();
            });
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
