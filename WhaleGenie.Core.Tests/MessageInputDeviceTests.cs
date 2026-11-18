using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The background device is checked against a real Win32 message queue: posting at a top-level
/// window looks successful even when a standard child control never receives the message, so a
/// stand-in that only records calls would not catch the failure this device exists to avoid.
/// </summary>
public class MessageInputDeviceTests
{
    [Fact]
    public void A_click_is_posted_to_the_child_under_the_client_point()
    {
        using var host = MessageWindow.Open();
        var device = new MessageInputDevice(host.Handle, NullDeviceLayer.Instance.Input);

        device.Click("left", 150, 40, 1, 0);

        var down = Assert.Single(host.WaitFor(TestMessage.LeftButtonDown));
        var up = Assert.Single(host.WaitFor(TestMessage.LeftButtonUp));
        Assert.Equal(host.RightChild, down.Window);
        Assert.Equal(host.RightChild, up.Window);
        Assert.Equal(50, down.X);
        Assert.Equal(40, down.Y);
        Assert.Equal(50, up.X);
        Assert.Equal(40, up.Y);
    }

    [Fact]
    public void A_key_without_a_click_finds_the_child_that_wants_text()
    {
        using var host = MessageWindow.Open();
        var device = new MessageInputDevice(host.Handle, NullDeviceLayer.Instance.Input);

        device.KeyPress("A", 0);

        Assert.Equal(host.LeftChild, Assert.Single(host.WaitFor(TestMessage.KeyDown)).Window);
        Assert.Equal(host.LeftChild, Assert.Single(host.WaitFor(TestMessage.KeyUp)).Window);
    }

    [Fact]
    public void A_key_after_a_click_goes_to_the_clicked_child()
    {
        using var host = MessageWindow.Open();
        var device = new MessageInputDevice(host.Handle, NullDeviceLayer.Instance.Input);

        device.Click("left", 150, 40, 1, 0);
        device.KeyPress("A", 0);

        Assert.Equal(host.RightChild, Assert.Single(host.WaitFor(TestMessage.KeyDown)).Window);
        Assert.Equal(host.RightChild, Assert.Single(host.WaitFor(TestMessage.KeyUp)).Window);
    }

    [Fact]
    public void The_wheel_carries_screen_pixels_for_the_child_it_targets()
    {
        using var host = MessageWindow.Open();
        var device = new MessageInputDevice(host.Handle, NullDeviceLayer.Instance.Input);

        device.Scroll("down", 120, 50, 40);

        var wheels = host.WaitFor(TestMessage.MouseWheel);
        Assert.True(wheels.Any(item => item.Window == host.LeftChild),
            $"left child {host.LeftChild}, host {host.Handle}, posted at "
            + string.Join(", ", wheels.Select(item => item.Window)));
        var wheel = wheels.First(item => item.Window == host.LeftChild);
        Assert.Equal(host.ScreenAt(50, 40).X, wheel.X);
        Assert.Equal(host.ScreenAt(50, 40).Y, wheel.Y);
    }

    [Fact]
    public void A_closed_target_is_reported_instead_of_being_ignored()
    {
        var device = new MessageInputDevice(new IntPtr(0x7FFFFFF0),
            NullDeviceLayer.Instance.Input);

        var refused = Assert.Throws<DeviceActionException>(() => device.KeyPress("A", 0));

        Assert.Equal(RunKeys.BackgroundPostFailed, refused.Key);
    }

    private enum TestMessage : uint
    {
        KeyDown = 0x0100,
        KeyUp = 0x0101,
        MouseMove = 0x0200,
        LeftButtonDown = 0x0201,
        LeftButtonUp = 0x0202,
        MouseWheel = 0x020A,
    }

    private sealed record Received(IntPtr Window, TestMessage Message, int X, int Y);

    private sealed class MessageWindow : IDisposable
    {
        private const uint WsPopup = 0x80000000;
        private const uint WsChild = 0x40000000;
        private const uint WsVisible = 0x10000000;
        private const uint WsTabStop = 0x00010000;
        private const uint WsExNoActivate = 0x08000000;
        private const uint WmClose = 0x0010;
        private const uint WmDestroy = 0x0002;
        private const uint WmGetDlgCode = 0x0087;
        private const int DlgcWantChars = 0x0080;

        private static readonly string ClassName = "WhaleGenieMessageTest" +
            Guid.NewGuid().ToString("N");
        private static readonly ConcurrentDictionary<IntPtr, MessageWindow> Windows = [];
        private static readonly WndProc Callback = HandleMessage;
        private static bool _registered;

        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new();
        private readonly ConcurrentQueue<Received> _received = [];
        private Exception? _failure;

        private MessageWindow()
        {
            _thread = new Thread(Run) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(5));
            if (_failure is not null)
            {
                throw _failure;
            }
        }

        public IntPtr Handle { get; private set; }

        public IntPtr LeftChild { get; private set; }

        public IntPtr RightChild { get; private set; }

        public static MessageWindow Open() => new();

        public IReadOnlyList<Received> WaitFor(TestMessage message)
        {
            SpinWait.SpinUntil(() => _received.Any(item => item.Message == message),
                TimeSpan.FromSeconds(2));
            return [.. _received.Where(item => item.Message == message)];
        }

        public (int X, int Y) ScreenAt(int x, int y)
        {
            var point = new NativePoint { X = x, Y = y };
            if (!ClientToScreen(Handle, ref point))
            {
                throw new InvalidOperationException("the test window has no client origin");
            }

            return (point.X, point.Y);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                PostMessage(Handle, WmClose, IntPtr.Zero, IntPtr.Zero);
                _thread.Join(TimeSpan.FromSeconds(2));
            }

            _ready.Dispose();
        }

        private void Run()
        {
            try
            {
                Register();
                Handle = CreateWindowEx(WsExNoActivate, ClassName, string.Empty,
                    WsPopup | WsVisible, 0, 0, 260, 160, IntPtr.Zero, IntPtr.Zero,
                    GetModuleHandle(null), IntPtr.Zero);
                if (Handle == IntPtr.Zero)
                {
                    throw new InvalidOperationException("the host window could not be created");
                }

                Windows[Handle] = this;
                LeftChild = CreateChild(0);
                RightChild = CreateChild(100);
                _ready.Set();

                while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            catch (Exception failure)
            {
                _failure = failure;
                _ready.Set();
            }
        }

        private IntPtr CreateChild(int x)
        {
            var child = CreateWindowEx(0, ClassName, string.Empty,
                WsChild | WsVisible | WsTabStop,
                x, 0, 100, 100, Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (child == IntPtr.Zero)
            {
                throw new InvalidOperationException("a child window could not be created");
            }

            Windows[child] = this;
            return child;
        }

        private static void Register()
        {
            if (_registered)
            {
                return;
            }

            var type = new WndClassEx
            {
                Size = Marshal.SizeOf<WndClassEx>(),
                WndProc = Callback,
                ClassName = MessageWindow.ClassName,
            };
            if (RegisterClassEx(ref type) == 0)
            {
                throw new InvalidOperationException("the test window class could not be registered");
            }

            _registered = true;
        }

        private static IntPtr HandleMessage(IntPtr window, uint message, IntPtr first, IntPtr second)
        {
            if (message == WmGetDlgCode)
            {
                return new IntPtr(DlgcWantChars);
            }

            if ((message is (uint)TestMessage.KeyDown or (uint)TestMessage.KeyUp
                    or (uint)TestMessage.MouseMove or (uint)TestMessage.LeftButtonDown
                    or (uint)TestMessage.LeftButtonUp or (uint)TestMessage.MouseWheel)
                && Windows.TryGetValue(window, out var listener))
            {
                var value = second.ToInt64();
                listener._received.Enqueue(new Received(window, (TestMessage)message,
                    Low(value), High(value)));
            }

            if (message == WmClose)
            {
                DestroyWindow(window);
                return IntPtr.Zero;
            }

            if (message == WmDestroy && Windows.TryGetValue(window, out var owner)
                && window == owner.Handle)
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }

            return DefWindowProc(window, message, first, second);
        }

        private static int Low(long value) => (short)(value & 0xFFFF);

        private static int High(long value) => (short)((value >> 16) & 0xFFFF);

        private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr first, IntPtr second);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WndClassEx
        {
            public int Size;
            public uint Style;
            public WndProc? WndProc;
            public int ClassExtra;
            public int WindowExtra;
            public IntPtr Instance;
            public IntPtr Icon;
            public IntPtr Cursor;
            public IntPtr Background;
            public string? MenuName;
            public string? ClassName;
            public IntPtr SmallIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr Window;
            public uint Message;
            public IntPtr First;
            public IntPtr Second;
            public uint Time;
            public NativePoint Point;
            public uint Private;
        }

        [DllImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WndClassEx type);

        [DllImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true,
            CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(uint extendedStyle, string className,
            string windowName, uint style, int x, int y, int width, int height, IntPtr parent,
            IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr first,
            IntPtr second);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out NativeMessage message, IntPtr window,
            uint first, uint last);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int code);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, uint message, IntPtr first,
            IntPtr second);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? name);
    }
}
