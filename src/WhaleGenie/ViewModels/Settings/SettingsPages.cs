using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// The pages of the settings window and the groups they belong to.
public enum SettingsSubPage
{
    General,
    Hotkeys,
    Screen,
    Input,
    Devices,
    MenuLauncher,
    WindowRules,
    WheelMenu,
    WheelActions,
    Theme,
}

/// <summary>
/// The three groups the window opens with. They are named at the top of the window, and the list on
/// the left only carries the pages that belong to the one that is open.
/// </summary>
public enum SettingsRoot
{
    System,
    Triggers,
    Appearance,
}
