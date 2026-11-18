using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Which piece driver-level input is still missing on a machine. Both answers come from the machine
/// — one out of the registry, one out of the folder the program sits in — so both are handed in here
/// and the combination is what is read.
/// </summary>
/// <remarks>
/// The collection keeps this class from running beside the checks that stand a library in for the
/// real one: both hand in the answers about this machine, and there is one of each to hand in.
/// </remarks>
[Collection("driver-level input")]
public class DriverInputTests : IDisposable
{
    private readonly Func<bool> _driverInstalled = DriverInput.DriverInstalled;
    private readonly Func<bool> _libraryPresent = DriverInput.LibraryPresent;

    /// <summary>Puts back what was there, so no other check runs against a made-up machine.</summary>
    public void Dispose()
    {
        DriverInput.DriverInstalled = _driverInstalled;
        DriverInput.LibraryPresent = _libraryPresent;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Without_the_driver_the_library_is_not_worth_looking_for()
    {
        // The library is useless on a machine with nothing to put its devices into, and the user has
        // one thing to install either way: the driver.
        DriverInput.DriverInstalled = () => false;
        DriverInput.LibraryPresent = () => false;

        Assert.Equal(DriverInputState.DriverMissing, DriverInput.Check());
    }

    [Fact]
    public void A_driver_without_the_library_that_came_with_the_program_is_reported_as_such()
    {
        DriverInput.DriverInstalled = () => true;
        DriverInput.LibraryPresent = () => false;

        Assert.Equal(DriverInputState.LibraryMissing, DriverInput.Check());
    }

    [Fact]
    public void Both_pieces_together_are_what_driver_level_input_waits_for()
    {
        DriverInput.DriverInstalled = () => true;
        DriverInput.LibraryPresent = () => true;

        Assert.Equal(DriverInputState.Ready, DriverInput.Check());
    }

    [Fact]
    public void Asking_about_this_machine_answers_rather_than_failing()
    {
        // Reading one registry key and looking for one file: whatever this machine has, asking has to
        // come back with one of the three answers rather than with an exception.
        Assert.True(Enum.IsDefined(DriverInput.Check()));
    }
}
