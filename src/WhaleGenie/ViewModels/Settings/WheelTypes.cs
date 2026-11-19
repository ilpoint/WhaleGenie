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

// Rows and choices the wheel settings page shows.
/// <summary>One scene in the wheel action manager's list.</summary>
public sealed record WheelSceneRow(string Key, string Summary, WheelScene Scene, bool IsGlobal);

/// <summary>One of the four buttons a wheel can be opened with, and the words for it.</summary>
public sealed record WheelLauncherChoice(string Binding, string Key)
{
    public override string ToString() => Strings.Get(Key);
}

/// <summary>One capacity of the third resident circle, named by the text table.</summary>
public sealed record WheelCapacityOption(int Value, string Key)
{
    public override string ToString() => Strings.Get(Key);
}
