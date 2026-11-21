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

// What each setting does when it changes, including the ones that need the program restarted.
public partial class SettingsViewModel
{
    /// <summary>The line above the button that restarts the program with that token.</summary>
    public string AdminStatus
        => Strings.Get(IsElevated ? "Settings.AdminYes" : "Settings.AdminNo");

    partial void OnSelectedLanguageChanged(LanguageOption value)
        => Strings.Current.Language = value.Language;

    /// <summary>Stores whether a start opens the project that was open last time.</summary>
    partial void OnLoadLastProjectAtStartChanged(bool value)
    {
        if (value != LocalSettings.LoadLastProjectAtStart())
        {
            LocalSettings.StoreLastProjectAtStart(value);
        }
    }

    /// <summary>Stores whether triggers listen as soon as the program starts.</summary>
    partial void OnSystemAtStartChanged(bool value)
    {
        if (value != LocalSettings.LoadSystemAtStart())
        {
            LocalSettings.StoreSystemAtStart(value);
        }
    }

    /// <summary>Stores whether enabled macros are listed first.</summary>
    partial void OnEnabledFirstChanged(bool value)
    {
        if (value != LocalSettings.LoadEnabledFirst())
        {
            LocalSettings.StoreEnabledFirst(value);
        }
    }

    /// <summary>
    /// Tells the engine first: the hook asks this on every key press, so a switch that only saved the
    /// answer would seem to do nothing until the next start.
    /// </summary>
    partial void OnIgnoreOwnInputChanged(bool value)
    {
        WhaleGenieInputGate.IgnoreOwnInput = value;
        if (value != LocalSettings.LoadIgnoreOwnInput())
        {
            LocalSettings.StoreIgnoreOwnInput(value);
        }
    }

    /// <summary>Stores whether the system switch is heard.</summary>
    partial void OnSoundSystemChanged(bool value)
    {
        if (value != LocalSettings.LoadSoundSystem())
        {
            LocalSettings.StoreSoundSystem(value);
        }
    }

    /// <summary>Stores whether arming and disarming a macro is heard.</summary>
    partial void OnSoundMacroChanged(bool value)
    {
        if (value != LocalSettings.LoadSoundMacro())
        {
            LocalSettings.StoreSoundMacro(value);
        }
    }

    /// <summary>The same, for the choice that holds the text recognition models in memory.</summary>
    partial void OnPreloadOcrChanged(bool value)
    {
        if (value != LocalSettings.LoadPreloadOcr())
        {
            LocalSettings.StorePreloadOcr(value);
        }
    }

    /// <summary>
    /// The same, for the choice that puts the virtual keyboard and mouse on the machine at startup.
    /// The engine hears about it when the program starts, which is also when it acts on it.
    /// </summary>
    partial void OnAttachDriverAtStartChanged(bool value)
    {
        if (value != LocalSettings.LoadDriverAttachAtStart())
        {
            LocalSettings.StoreDriverAttachAtStart(value);
        }
    }

    /// <summary>
    /// Gives the engine the new way, but only when it really changed: opening the window reads what
    /// the engine is using into this property, and that must not count as the user asking for
    /// something.
    /// </summary>
    partial void OnSelectedWindowWayChanged(CaptureOption value)
    {
        if (value.Way != CaptureChoice.Current.Window)
        {
            CaptureSetup.StoreWindow(value.Way);
        }
    }

    /// <summary>The same for the way the desktop is read.</summary>
    partial void OnSelectedScreenWayChanged(CaptureOption value)
    {
        if (value.Way != CaptureChoice.Current.Screen)
        {
            CaptureSetup.StoreScreen(value.Way);
        }
    }
}
