using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;


namespace WhaleGenie.Tests;

// Alt and X write the position the action asks for.
public partial class WindowRulePickerTests
{
    [Fact]
    public void AltX_writes_the_action_window_position()
    {
        Ui.Run(() =>
        {
            var devices = new ChecksDevices { Screen = new ScreenAt(new ScreenPoint(321, 123)) };
            var window = new ActionEditorWindow(null, ActionCatalog.Definitions, [], [])
            {
                Devices = devices,
            };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();

                var view = window.View;
                view.SelectAction("vision.getPixel");
                Dispatcher.UIThread.RunJobs();

                window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Alt);
                Dispatcher.UIThread.RunJobs();

                var action = Assert.IsType<AddActionViewModel>(view.DataContext);
                Assert.Equal("321", action.Parameters
                    .First(parameter => parameter.Definition.Name == "x").CurrentText);
                Assert.Equal("123", action.Parameters
                    .First(parameter => parameter.Definition.Name == "y").CurrentText);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class KeyHook : IInputHook
    {
        public event Action<KeyHookEvent>? KeyDown;

        public event Action<KeyHookEvent>? KeyUp;

        private Action<MouseHookEvent>? _mouseDown;

        public event Action<MouseHookEvent>? MouseDown
        {
            add => _mouseDown += value;
            remove => _mouseDown -= value;
        }

        public event Action<MouseHookEvent>? MouseUp
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedHookEvent>? Moved
        {
            add { }
            remove { }
        }

        public event Action<MouseMovedRelativeHookEvent>? MovedRelative
        {
            add { }
            remove { }
        }

        public event Action<WheelHookEvent>? Wheel
        {
            add { }
            remove { }
        }

        /// <summary>
        /// 按下鼠标左键，返回它有没有被拾取器吞掉。取窗口时这一下要放行：人还要去点别的窗口、
        /// 点任务栏启动应用，所以"吞掉"才是错的。
        /// </summary>
        public bool Click(int x, int y, HookMouseButton button = HookMouseButton.Left)
        {
            var e = new MouseHookEvent(button, x, y, injected: false);
            _mouseDown?.Invoke(e);
            return e.Suppress;
        }

        public bool Press(HookKey key, bool down)
        {
            var e = new KeyHookEvent(key, injected: false);
            if (down)
            {
                KeyDown?.Invoke(e);
            }
            else
            {
                KeyUp?.Invoke(e);
            }

            return e.Suppress;
        }
    }

    private sealed class ScreenAt(ScreenPoint cursor) : IScreenDevice
    {
        public ScreenLayout Desktop { get; } = new(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        public ScreenPoint Cursor { get; } = cursor;

        public PixelColor PixelAt(int x, int y) => default;

        public ImageFrame Capture(int x, int y, int width, int height)
            => new(width, height, new byte[width * height * 4]);

        public ScreenShot Capture(ScreenCaptureRequest request)
            => NullDeviceLayer.Instance.Screen.Capture(request);
    }

    private sealed class UiAt(UiElementInfo element) : IUiDevice
    {
        public bool Exists(UiQuery query, int timeoutMs)
            => NullDeviceLayer.Instance.Ui.Exists(query, timeoutMs);

        public UiElementInfo? ElementAt(int x, int y) => element;

        public bool Click(UiQuery query, string button)
            => NullDeviceLayer.Instance.Ui.Click(query, button);

        public bool Invoke(UiQuery query) => NullDeviceLayer.Instance.Ui.Invoke(query);

        public UiElementFacts? Facts(UiQuery query)
            => NullDeviceLayer.Instance.Ui.Facts(query);

        public bool FocusWindow(UiQuery query)
            => NullDeviceLayer.Instance.Ui.FocusWindow(query);

        public bool SetText(UiQuery query, string text, bool clearFirst)
            => NullDeviceLayer.Instance.Ui.SetText(query, text, clearFirst);

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
            => NullDeviceLayer.Instance.Ui.FindAll(query, limit);

        public bool Select(UiQuery query, string text, int itemIndex)
            => NullDeviceLayer.Instance.Ui.Select(query, text, itemIndex);

        public bool SetChecked(UiQuery query, bool? state)
            => NullDeviceLayer.Instance.Ui.SetChecked(query, state);

        public bool SetExpanded(UiQuery query, string action)
            => NullDeviceLayer.Instance.Ui.SetExpanded(query, action);

        public bool ScrollIntoView(UiQuery query)
            => NullDeviceLayer.Instance.Ui.ScrollIntoView(query);

        public UiTable ReadTable(UiQuery query, int limit)
            => NullDeviceLayer.Instance.Ui.ReadTable(query, limit);
    }
}
