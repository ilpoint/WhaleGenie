using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// Odds and ends about this machine: who is signed in, where things live, and the handful of
/// things a person can ask it to do to itself from the Start menu's power button.
/// </summary>
public sealed partial class WindowsSystemDevice : ISystemDevice
{
}
