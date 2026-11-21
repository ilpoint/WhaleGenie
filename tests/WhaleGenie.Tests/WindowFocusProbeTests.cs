using Avalonia.Controls;
using WhaleGenie.Runtime;

namespace WhaleGenie.Tests;

/// <summary>
/// The trigger host asks the window that really has the keyboard. The web shell and the native main
/// window each copy their focus state into ITriggerFocusProbe; this wrapper is the one place the
/// active one is asked.
/// </summary>
public class WindowFocusProbeTests
{
    [Fact]
    public void The_window_handed_in_answers()
    {
        Ui.Run(() =>
        {
            var window = new ProbeWindow();
            var probe = new WindowFocusProbe(() => window);

            Assert.False(probe.IsTextInputFocused);
            window.IsTextInputFocused = true;
            Assert.True(probe.IsTextInputFocused);

            window.Close();
            return true;
        });
    }

    [Fact]
    public void No_window_is_not_typing()
    {
        var probe = new WindowFocusProbe(() => null);
        Assert.False(probe.IsTextInputFocused);
    }

    private sealed class ProbeWindow : Window, ITriggerFocusProbe
    {
        public bool IsTextInputFocused { get; set; }
    }
}
