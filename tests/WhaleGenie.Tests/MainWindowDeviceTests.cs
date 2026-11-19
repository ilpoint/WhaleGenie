using System;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The window works against the layer it was handed, not the program-wide one. A check can hand it
/// a desktop and a set of window events of its own, which is what lets the menu, wheel and floating
/// panel be exercised without a real machine behind them.
/// </summary>
public class MainWindowDeviceTests
{
    [Fact]
    public void The_window_uses_the_device_layer_it_was_given()
    {
        var devices = new ChecksDevices();
        var events = new CountingWindowEvents();
        devices.WindowEvents = events;

        Ui.Run(() =>
        {
            var variables = new VariableSession();
            var runtime = new TriggerRuntime(devices, variables);
            var window = new MainWindow(runtime)
            {
                DataContext = new MainViewModel(variables: variables) { IsRunning = false },
            };

            Assert.Same(devices, window.DevicesForCheck);

            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, events.ForegroundAdded);
            Assert.Equal(1, events.BoundsAdded);

            window.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, events.ForegroundRemoved);
            Assert.Equal(1, events.BoundsRemoved);
        });
    }

    /// <summary>The one event the window subscribes to while it is open, counted both ways.</summary>
    private sealed class CountingWindowEvents : IWindowEvents
    {
        public bool Available => true;

        public int ForegroundAdded { get; private set; }

        public int ForegroundRemoved { get; private set; }

        public int BoundsAdded { get; private set; }

        public int BoundsRemoved { get; private set; }

        public event Action? Changed
        {
            add { }
            remove { }
        }

        public event Action? ForegroundChanged
        {
            add => ForegroundAdded++;
            remove => ForegroundRemoved++;
        }

        public event Action<long>? BoundsChanged
        {
            add => BoundsAdded++;
            remove => BoundsRemoved++;
        }

        public void Start()
        {
        }

        public void Dispose()
        {
        }
    }
}
