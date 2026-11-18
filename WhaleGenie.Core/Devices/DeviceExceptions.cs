using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// The two failures a device reports: one it cannot do, and one that went wrong.
/// <summary>Thrown when an action needs something this machine cannot give it.</summary>
public sealed class DeviceUnavailableException(string capability)
    : Exception($"The device layer does not provide {capability}.")
{
    /// <summary>What was missing, for example <c>screen</c> or <c>ocr</c>.</summary>
    public string Capability { get; } = capability;
}

/// <summary>
/// Thrown when the device is there but the request makes no sense. The key is translated by
/// the interface, the way every other run failure is.
/// </summary>
public sealed class DeviceActionException(string key, string detail = "")
    : Exception(key)
{
    public string Key { get; } = key;

    public string Detail { get; } = detail;
}
