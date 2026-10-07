using Viktor.Core.Devices;
using Viktor.Core.Devices.Platform;

namespace Viktor.Core.Tests;

/// <summary>
/// Which virtual keyboard and mouse a run is handed, and when the pair leaves the machine. The
/// machine answers only the pair that got on its bus first — a second pair arrives attached and then
/// does nothing — so the program keeps one. Nothing here talks to a server: what is checked is who
/// holds the pair, not what was sent through it.
/// </summary>
public class DriverDeviceSharingTests
{
    private static InputRoute Driver => new(InputDelivery.Driver, 0);

    [Fact]
    public void Two_runs_in_the_program_are_given_the_same_virtual_keyboard_and_mouse()
    {
        var front = new FakeDeviceLayer();

        using var first = new WindowsInputRouter(front);
        using var second = new WindowsInputRouter(front);

        Assert.Same(first.For(Driver), second.For(Driver));
    }

    [Fact]
    public void A_run_is_given_the_same_device_for_every_step_that_asks_for_it()
    {
        using var router = new WindowsInputRouter(new FakeDeviceLayer());

        Assert.Same(router.For(Driver), router.For(Driver));
    }

    [Fact]
    public void The_devices_stay_on_the_machine_until_the_last_run_lets_go_of_them()
    {
        var front = new FakeDeviceLayer();

        var first = new WindowsInputRouter(front);
        var device = first.For(Driver);

        var second = new WindowsInputRouter(front);
        Assert.Same(device, second.For(Driver));

        // The run that opened a device layer of its own is done, while the other is still using the
        // keyboard and mouse: taking them off the machine here would leave that run typing into
        // nothing.
        first.Dispose();

        var third = new WindowsInputRouter(front);
        Assert.Same(device, third.For(Driver));

        second.Dispose();
        third.Dispose();

        // Nobody is holding them now, so the next run gets the pair that is put on the machine for
        // it rather than one left over from before.
        var later = new WindowsInputRouter(front);
        Assert.NotSame(device, later.For(Driver));
        later.Dispose();
    }

    [Fact]
    public void A_run_that_never_asked_for_driver_input_leaves_the_devices_where_they_are()
    {
        var front = new FakeDeviceLayer();

        var holder = new WindowsInputRouter(front);
        var device = holder.For(Driver);

        using (var other = new WindowsInputRouter(front))
        {
            _ = other.For(InputRoute.Front);
        }

        var next = new WindowsInputRouter(front);
        Assert.Same(device, next.For(Driver));

        holder.Dispose();
        next.Dispose();
    }
}
