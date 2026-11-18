using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The two promises of this first wheel step: a middle press still opens the macro menu exactly as
/// before, and the wheel only appears after a held right button has actually been moved.
/// </summary>
public partial class WheelMenuTests
{
    private static readonly WheelSlotRef Top = new(WheelRing.Inner, 0);
    private static readonly WheelSlotRef Right = new(WheelRing.Inner, 2);
}
