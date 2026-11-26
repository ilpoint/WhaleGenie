using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// Finds and drives windows and controls through Windows UI Automation. Elements are found
/// by what the user sees, so a macro reads the way the screen reads.
/// </summary>
public sealed partial class FlaUiDevice : IUiDevice, IDisposable
{
    /// <summary>
    /// How far up the tree the picker looks for the window an element sits in. Deep visual trees
    /// are real — a browser or an editor can nest forty panels under one window — so the walk is
    /// bounded generously and simply gives up when a provider never produces a window.
    /// </summary>
    private const int WindowHops = 40;

    private readonly Lazy<UIA3Automation> _automation = new(() => new UIA3Automation());

    /// <summary>
    /// Used for the button UI Automation has no click of its own for, by clicking the
    /// element's own clickable point.
    /// </summary>
    private readonly Lazy<IInputDevice> _input;
}
