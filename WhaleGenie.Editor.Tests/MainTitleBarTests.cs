using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class MainTitleBarTests
{
    [Fact]
    public void The_always_on_top_button_stays_clear_of_the_system_buttons_at_the_minimum_width()
    {
        Ui.Run(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.Width = window.MinWidth;
            Dispatcher.UIThread.RunJobs();

            var pin = Assert.IsType<Button>(window.FindControl<Button>("AlwaysOnTopButton"));
            var corner = pin.TranslatePoint(new Point(0, 0), window);
            Assert.True(corner is not null, "the pin button is not laid out in the window");
            Assert.True(corner.Value.X + pin.Bounds.Width <= window.ClientSize.Width - 138,
                $"pin ends at {corner.Value.X + pin.Bounds.Width}, window is {window.ClientSize.Width} wide");
            window.Close();
        });
    }
}
