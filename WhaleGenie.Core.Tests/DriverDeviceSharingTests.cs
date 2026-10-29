using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Which virtual keyboard and mouse a run is handed, and when the pair leaves the machine. The
/// machine answers only the pair that got on its bus first — a second pair arrives attached and then
/// does nothing — so the program keeps one, and keeps it on the machine until the program itself is
/// done with driver-level input. Nothing here talks to a server: what is checked is who holds the
/// pair, not what was sent through it.
/// </summary>
/// <remarks>
/// The collection keeps this class from running beside the checks that stand a library in for the one
/// this program loads, since both reach into the same program-wide pair.
/// </remarks>
[Collection("driver-level input")]
public class DriverDeviceSharingTests
{
    private static InputRoute Driver => new(InputDelivery.Driver, 0);

    /// <summary>
    /// One stand-in for the machine, used as both the front device and the screen: the pair of
    /// virtual devices is put on the bus by a run, and where the pointer is is the screen's answer,
    /// so the same stand-in can be both without a second one being written.
    /// </summary>
    private static WindowsInputRouter Router(FakeDeviceLayer devices) => new(devices, devices.Screen);

    [Fact]
    public void Two_runs_in_the_program_are_given_the_same_virtual_keyboard_and_mouse()
    {
        var front = new FakeDeviceLayer();

        using var first = Router(front);
        using var second = Router(front);

        Assert.Same(first.For(Driver), second.For(Driver));
    }

    [Fact]
    public void A_run_is_given_the_same_device_for_every_step_that_asks_for_it()
    {
        using var router = Router(new FakeDeviceLayer());

        Assert.Same(router.For(Driver), router.For(Driver));
    }

    [Fact]
    public void The_devices_stay_on_the_machine_after_a_run_is_done_with_them()
    {
        var front = new FakeDeviceLayer();

        var first = Router(front);
        var device = first.For(Driver);

        var second = Router(front);
        Assert.Same(device, second.For(Driver));

        // A run that is over is not the end of the pair: putting it on the machine is what the first
        // driver-level step of every run would otherwise wait for.
        first.Dispose();

        var third = Router(front);
        Assert.Same(device, third.For(Driver));

        second.Dispose();
        third.Dispose();
    }

    [Fact]
    public void Letting_go_of_the_pair_is_what_takes_it_off_the_machine()
    {
        var front = new FakeDeviceLayer();

        using var router = Router(front);
        var device = router.For(Driver);

        // The way out of the program, or handing over to a copy running with higher rights: nobody
        // is left to use the pair, and the machine has to answer whoever asks next.
        SharedDriverInput.Release();

        var later = Router(front);
        Assert.NotSame(device, later.For(Driver));
        later.Dispose();
    }

    [Fact]
    public void A_run_that_never_asked_for_driver_input_leaves_the_devices_where_they_are()
    {
        var front = new FakeDeviceLayer();

        var holder = Router(front);
        var device = holder.For(Driver);

        using (var other = Router(front))
        {
            _ = other.For(InputRoute.Front);
        }

        var next = Router(front);
        Assert.Same(device, next.For(Driver));

        holder.Dispose();
        next.Dispose();
    }
}
