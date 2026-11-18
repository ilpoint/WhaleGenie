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

// Entries the settings pickers show: a language, a theme and a way of capturing.
/// <summary>One entry of the language picker, named in its own language.</summary>
public sealed record LanguageOption(Language Language, string Display)
{
    public override string ToString() => Display;
}

/// <summary>One entry of the theme picker; the name is read when the entry is shown.</summary>
public sealed record ThemeOption(ThemeMode Mode, string Key)
{
    public override string ToString() => Strings.Get(Key);
}

/// <summary>
/// One entry of a capture picker. What it reads as comes from the text table rather than being
/// copied here, so the two pickers and the ways named in a failure are named the same way.
/// </summary>
public sealed record CaptureOption(CaptureWay Way, string Key)
{
    public override string ToString() => Strings.Get(Key);
}
