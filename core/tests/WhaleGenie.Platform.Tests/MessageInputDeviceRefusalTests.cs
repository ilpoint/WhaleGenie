using System;

using WhaleGenie.Core.Devices;
using WhaleGenie.Platform.Windows;
using WhaleGenie.TestSupport;

namespace WhaleGenie.Platform.Tests;

/// <summary>
/// The device refuses a relative move on its own as well: a step that reached it anyway is answered
/// with the same key rather than with an offset sent as "move to that point".
/// </summary>
public class MessageInputDeviceRefusalTests
{
    /// <summary>
    /// 设备自己那一层也拒：真到了它手上，宁可报同一句话，也不要把偏移当成"移到那个点"发出去。
    /// </summary>
    [Fact]
    public void Posted_messages_refuse_a_relative_move_at_the_device_too()
    {
        var device = new MessageInputDevice(IntPtr.Zero, new FakeDeviceLayer().Input);
        var refused = Assert.Throws<DeviceActionException>(
            () => device.MoveMouseRelative(200, 0, 0));
        Assert.Equal("Run.BackgroundNeedsPointer", refused.Key);
    }
}
