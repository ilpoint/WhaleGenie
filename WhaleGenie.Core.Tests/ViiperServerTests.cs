using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Driver-level input with the library written down instead of loaded: when the server this program
/// runs in itself comes up and goes down again, which devices go on the machine and which reports
/// they carry. Nothing here puts a device on anything — what the engine asks of the library is what
/// is read, and the real library is what a person runs on their own machine.
/// </summary>
/// <remarks>
/// The collection keeps this class from running beside the checks that hand out the program's one
/// virtual keyboard and mouse: both reach into what stands for the library, and the program has one.
/// </remarks>
[Collection("driver-level input")]
public class ViiperServerTests : IDisposable
{
    private readonly IViiperNative _native = ViiperServer.Native;
    private readonly bool _leaving = ViiperServer.Leaving;
    private readonly Func<bool> _driverInstalled = DriverInput.DriverInstalled;
    private readonly Func<bool> _libraryPresent = DriverInput.LibraryPresent;

    private readonly FakeDeviceLayer _machine = new();

    private readonly FakeViiper _library;

    public ViiperServerTests()
    {
        _library = new FakeViiper(_machine);
        ViiperServer.Native = _library;
        DriverInput.DriverInstalled = () => true;
        DriverInput.LibraryPresent = () => true;
    }

    /// <summary>
    /// Puts the machine back as it was, and closes whatever this check left running: the server and
    /// the devices belong to the program rather than to a check.
    /// </summary>
    public void Dispose()
    {
        SharedDriverInput.Release();
        ViiperServer.Stop();

        ViiperServer.Native = _native;
        ViiperServer.Leaving = _leaving;
        DriverInput.DriverInstalled = _driverInstalled;
        DriverInput.LibraryPresent = _libraryPresent;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void The_server_comes_up_the_first_time_it_is_asked_for_and_is_then_shared()
    {
        var first = ViiperServer.Ensure();
        var second = ViiperServer.Ensure();

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, _library.Opens);
        Assert.Equal(1, _library.Buses);
    }

    [Fact]
    public void The_server_listens_on_this_machine_only_and_lets_the_system_choose_the_port()
    {
        // Loopback, so Windows does not ask the user to let this program through the firewall for a
        // connection no other machine ever makes; an unnamed port, so the old VIIPER server program
        // sitting on its own port cannot keep this one from coming up.
        _ = ViiperServer.Ensure();

        Assert.StartsWith("127.0.0.1:", _library.Address, StringComparison.Ordinal);
        Assert.EndsWith(":0", _library.Address, StringComparison.Ordinal);
    }

    [Fact]
    public void A_server_that_will_not_come_up_is_reported_rather_than_promised()
    {
        _library.ServerStarts = false;

        Assert.Null(ViiperServer.Ensure());

        var failure = Assert.Throws<DeviceActionException>(() => ViiperLink.Open(_machine));

        Assert.Equal("Run.NoDriverServer", failure.Key);
    }

    [Fact]
    public void On_the_way_out_no_server_is_started_any_more()
    {
        _ = ViiperServer.Ensure();
        ViiperServer.Stop();

        Assert.True(ViiperServer.Leaving);
        Assert.Equal(1, _library.Closes);
        Assert.Null(ViiperServer.Ensure());
        Assert.Equal(1, _library.Opens);
    }

    [Fact]
    public void A_hand_over_still_allows_the_next_start()
    {
        // The elevated copy takes over and this one closes the server it started, so the new copy's
        // own server is not closed right after it starts. But the user can say no at the prompt and
        // this copy is then the one still running, so this is not the way out.
        var first = ViiperServer.Ensure();
        ViiperServer.HandOver();

        Assert.False(ViiperServer.Leaving);

        var second = ViiperServer.Ensure();

        Assert.NotNull(second);
        Assert.NotSame(first, second);
        Assert.Equal(2, _library.Opens);
        Assert.Equal(1, _library.Closes);
    }

    [Fact]
    public void The_keyboard_and_mouse_go_on_the_machine_when_a_step_first_needs_them()
    {
        using var link = ViiperLink.Open(_machine);

        Assert.Equal(1, _library.Keyboards);
        Assert.Equal(1, _library.Mice);
        Assert.Equal(0, _library.Pads);
    }

    [Fact]
    public void Every_device_is_attached_to_this_machine_as_it_is_created()
    {
        // A device that is put on a bus and never attached is one the desktop never hears: it is
        // the difference between a virtual keyboard that types and one that does nothing.
        using var link = ViiperLink.Open(_machine);
        link.ConnectGamepad();

        Assert.Equal([true, true, true], _library.AutoAttach);
    }

    [Fact]
    public void Half_a_pair_is_taken_off_the_machine_again()
    {
        // The machine answers only the pair that got to it first, so one device on its own would be
        // the answer — and nothing would come of the step that needed the pair.
        _library.MouseStarts = false;

        var failure = Assert.Throws<DeviceActionException>(() => ViiperLink.Open(_machine));

        Assert.Equal("Run.NoDriverServer", failure.Key);
        Assert.Equal(1, _library.KeyboardsRemoved);
    }

    [Fact]
    public void A_device_that_cannot_be_created_is_tried_again_on_a_fresh_bus()
    {
        // The server takes an empty bus away after a few seconds, and the pair a finished run took
        // off the machine leaves one behind: the bus this program remembers is then gone.
        _library.KeyboardMisses = 1;

        using var link = ViiperLink.Open(_machine);

        Assert.Equal(1, _library.Keyboards);
        Assert.Equal(1, _library.BusesRemoved);
        Assert.Equal(2, _library.Buses);
    }

    [Fact]
    public void A_key_travels_in_the_bit_its_usage_code_names()
    {
        using var link = ViiperLink.Open(_machine);

        // Shift and S: the modifier keeps its own byte, the key is one bit of the 256 the report has.
        link.SendKeyboard(LibViiper.ModifierLeftShift, [(byte)HidUsage.S, (byte)HidUsage.A]);

        var state = Assert.IsType<LibViiper.KeyboardDeviceState>(_library.Keyboard);

        Assert.Equal(LibViiper.ModifierLeftShift, state.Modifiers);
        Assert.Equal(0x40, state.Keys[(byte)HidUsage.S / 8]);
        Assert.Equal(0x10, state.Keys[(byte)HidUsage.A / 8]);
    }

    [Fact]
    public void A_key_that_was_let_go_of_is_not_in_the_next_report()
    {
        using var link = ViiperLink.Open(_machine);

        link.SendKeyboard(0, [(byte)HidUsage.A]);
        link.SendKeyboard(0, []);

        var state = Assert.IsType<LibViiper.KeyboardDeviceState>(_library.Keyboard);

        Assert.Equal(new byte[32], state.Keys);
    }

    [Fact]
    public void A_mouse_report_carries_how_far_to_go_and_what_is_held()
    {
        using var link = ViiperLink.Open(_machine);

        link.SendMouse(LibViiper.MouseRight, 10, -5, 1, 0);

        var state = Assert.IsType<LibViiper.MouseDeviceState>(_library.Mouse);

        Assert.Equal(LibViiper.MouseRight, state.Buttons);
        Assert.Equal(10, state.Dx);
        Assert.Equal(-5, state.Dy);
        Assert.Equal(1, state.Wheel);
        Assert.Equal(0, state.Pan);
    }

    [Fact]
    public void The_controller_goes_on_the_machine_when_a_macro_asks_for_one()
    {
        using var link = ViiperLink.Open(_machine);

        link.ConnectGamepad();
        link.ConnectGamepad();

        Assert.Equal(1, _library.Pads);
    }

    [Fact]
    public void A_controller_report_is_the_whole_controller()
    {
        using var link = ViiperLink.Open(_machine);

        link.ConnectGamepad();
        link.SendGamepad(GamepadState.Neutral with
        {
            Buttons = GamepadButtons.A | GamepadButtons.RightBumper,
            LeftTrigger = 100,
            RightTrigger = 0,
            LeftX = 50,
            LeftY = -100,
        });

        var state = Assert.IsType<LibViiper.Xbox360DeviceState>(_library.Pad);

        Assert.Equal(LibViiper.PadA | LibViiper.PadRightShoulder, state.Buttons);
        Assert.Equal(255, state.LeftTrigger);
        Assert.Equal(0, state.RightTrigger);
        Assert.Equal(16383, state.LeftX);
        Assert.Equal(-32767, state.LeftY);
    }

    [Fact]
    public void A_controller_no_macro_asked_for_is_not_on_the_machine()
    {
        using var link = ViiperLink.Open(_machine);

        var failure = Assert.Throws<DeviceActionException>(() => link.SendGamepad(GamepadState.Neutral));

        Assert.Equal("Run.NoGamepad", failure.Key);
    }

    [Fact]
    public async Task The_pair_goes_on_the_machine_at_startup_only_when_the_settings_window_asked()
    {
        var off = new ViiperDriver(() => _machine);
        await off.WarmUp();

        Assert.Equal(0, _library.Opens);

        var on = new ViiperDriver(() => _machine) { AttachAtStart = true };
        await on.WarmUp();

        Assert.Equal(1, _library.Keyboards);
        Assert.Equal(1, _library.Mice);
    }

    [Fact]
    public async Task A_warm_up_that_cannot_put_the_pair_on_the_machine_is_not_reported()
    {
        // Nobody asked for driver-level input yet, so nobody is told about a wait this machine
        // cannot pay: the step that needs it says what is missing and what to do about it.
        DriverInput.DriverInstalled = () => false;

        await new ViiperDriver(() => _machine) { AttachAtStart = true }.WarmUp();

        Assert.Equal(0, _library.Opens);
    }

    [Fact]
    public async Task The_way_out_takes_the_devices_off_the_machine_before_the_server_goes()
    {
        await new ViiperDriver(() => _machine) { AttachAtStart = true }.WarmUp();
        SharedDriverInput.Take(_machine).Connect();

        new ViiperDriver(() => _machine).Stop();

        Assert.Equal(
            ["keyboard off", "mouse off", "pad off", "close"],
            _library.Calls.Where(call => call is "keyboard off" or "mouse off" or "pad off" or "close"));
    }

    [Fact]
    public void A_report_sent_after_the_server_went_fails_the_step_rather_than_the_program()
    {
        // The library panics when it is handed a device that is already off the machine, and a panic
        // in somebody's shared library takes the whole process down with it — nothing managed can
        // catch that. A run can still be sending when the program is on its way out, so a report that
        // arrives after that has to be refused here instead of being passed on.
        using var link = ViiperLink.Open(_machine);
        ViiperServer.Stop();

        var failure = Assert.Throws<DeviceActionException>(
            () => link.SendKeyboard(0, [(byte)HidUsage.A]));

        Assert.Equal("Run.NoDriverAttached", failure.Key);
        Assert.Null(_library.Keyboard);
    }

    [Fact]
    public void Without_the_driver_there_is_nothing_to_put_on_the_machine()
    {
        DriverInput.DriverInstalled = () => false;

        var failure = Assert.Throws<DeviceActionException>(() => ViiperLink.Open(_machine));

        Assert.Equal("Run.NoDriver", failure.Key);
        Assert.Equal(0, _library.Opens);
    }

    [Fact]
    public void A_program_that_was_unpacked_without_the_library_says_so()
    {
        DriverInput.LibraryPresent = () => false;

        var failure = Assert.Throws<DeviceActionException>(() => ViiperLink.Open(_machine));

        Assert.Equal("Run.NoDriverLibrary", failure.Key);
        Assert.Equal(0, _library.Opens);
    }

    /// <summary>
    /// The library, written down instead of loaded. Every call is remembered, and each of them can be
    /// told to fail, so what a run does when the library says no is readable as well.
    /// </summary>
    private sealed class FakeViiper : IViiperNative
    {
        private readonly FakeDeviceLayer _machine;

        public FakeViiper(FakeDeviceLayer machine) => _machine = machine;

        /// <summary>What was asked for, in the order it was asked for.</summary>
        public List<string> Calls { get; } = [];

        /// <summary>Where the server was told to listen.</summary>
        public string? Address { get; private set; }

        public bool ServerStarts { get; set; } = true;

        public bool MouseStarts { get; set; } = true;

        /// <summary>How many creations of that device fail before one works.</summary>
        public int KeyboardMisses { get; set; }

        public int Opens { get; private set; }

        public int Closes { get; private set; }

        public int Buses { get; private set; }

        public int BusesRemoved { get; private set; }

        public int Keyboards { get; private set; }

        public int Mice { get; private set; }

        public int Pads { get; private set; }

        public int KeyboardsRemoved { get; private set; }

        /// <summary>Whether each device was attached to this machine as it was created.</summary>
        public List<bool> AutoAttach { get; } = [];

        /// <summary>The last report of each kind that was sent.</summary>
        public LibViiper.KeyboardDeviceState? Keyboard { get; private set; }

        public LibViiper.MouseDeviceState? Mouse { get; private set; }

        public LibViiper.Xbox360DeviceState? Pad { get; private set; }

        public IntPtr OpenServer(string address)
        {
            Address = address;
            Calls.Add("open");

            if (!ServerStarts)
            {
                return IntPtr.Zero;
            }

            return (IntPtr)(++Opens);
        }

        public bool CloseServer(IntPtr server)
        {
            Calls.Add("close");
            Closes++;
            return true;
        }

        public bool CreateBus(IntPtr server, ref uint busId)
        {
            Calls.Add("bus");
            busId = (uint)++Buses;
            return true;
        }

        public bool RemoveBus(IntPtr server, uint busId)
        {
            Calls.Add("bus off");
            BusesRemoved++;
            return true;
        }

        public bool CreateKeyboard(IntPtr server, uint busId, bool autoAttach, out IntPtr keyboard)
        {
            AutoAttach.Add(autoAttach);

            if (KeyboardMisses > 0)
            {
                KeyboardMisses--;
                keyboard = IntPtr.Zero;
                return false;
            }

            Calls.Add("keyboard");
            keyboard = (IntPtr)(200 + ++Keyboards);
            return true;
        }

        public bool SetKeyboard(IntPtr keyboard, LibViiper.KeyboardDeviceState state)
        {
            Keyboard = state;
            return true;
        }

        public bool RemoveKeyboard(IntPtr keyboard)
        {
            Calls.Add("keyboard off");
            KeyboardsRemoved++;
            return true;
        }

        public bool CreateMouse(IntPtr server, uint busId, bool autoAttach, out IntPtr mouse)
        {
            AutoAttach.Add(autoAttach);

            if (!MouseStarts)
            {
                mouse = IntPtr.Zero;
                return false;
            }

            Calls.Add("mouse");
            mouse = (IntPtr)(300 + ++Mice);
            return true;
        }

        public bool SetMouse(IntPtr mouse, LibViiper.MouseDeviceState state)
        {
            Mouse = state;

            // The machine moves the pointer by what a report says — which is what ends the wait for a
            // device that has just been put on it: the pointer being moved and put back is the only
            // thing that can say the machine is listening.
            _machine.Cursor = new ScreenPoint(_machine.Cursor.X + state.Dx, _machine.Cursor.Y + state.Dy);
            return true;
        }

        public bool RemoveMouse(IntPtr mouse)
        {
            Calls.Add("mouse off");
            return true;
        }

        public bool CreateXbox360(IntPtr server, uint busId, bool autoAttach, out IntPtr pad)
        {
            AutoAttach.Add(autoAttach);
            Calls.Add("pad");
            pad = (IntPtr)(400 + ++Pads);
            return true;
        }

        public bool SetXbox360(IntPtr pad, LibViiper.Xbox360DeviceState state)
        {
            Pad = state;
            return true;
        }

        public bool RemoveXbox360(IntPtr pad)
        {
            Calls.Add("pad off");
            return true;
        }
    }
}
