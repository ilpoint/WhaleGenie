using System;
using System.Globalization;


namespace WhaleGenie.Core.Devices;

// A window and the rule that names it.
/// <summary>
/// How a window is picked out of the ones that are open. A title is what a person sees, but it
/// changes with the document and the language, so the program that owns the window and the class
/// it registered are the sturdier things to name.
/// </summary>
public enum WindowMatch
{
    /// <summary>Part of the window title, which is what a person reads off the title bar.</summary>
    Title,

    /// <summary>Part of the name of the program that owns the window, without ".exe".</summary>
    Process,

    /// <summary>The window class the owning program registered, such as "Notepad".</summary>
    ClassName,

    /// <summary>The handle of one exact window instance, valid only while that window exists.</summary>
    Handle,
}

/// <summary>
/// An open top-level window: what it is called, where it sits, and whether it is shrunk or
/// filling the screen. The handle is what the window actions are carried out on.
/// </summary>
public sealed record WindowInfo(
    long Handle,
    string Title,
    ScreenPoint Location,
    ScreenSize Size,
    bool Minimized,
    bool Maximized);
