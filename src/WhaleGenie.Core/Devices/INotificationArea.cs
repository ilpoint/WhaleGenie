using System;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// The icon a program keeps in the notification area, and what a click on it means.
///
/// It is separated from the one Windows implementation of it so the policy above it — that closing
/// the window puts the program out of the way rather than ending it — can be read and checked
/// without a notification area no test machine has.
/// </summary>
public interface INotificationArea : IDisposable
{
    /// <summary>The user asked for the program back: a click on the icon, or the menu's first entry.</summary>
    event Action? Activated;

    /// <summary>The user asked the program to stop: the menu's last entry.</summary>
    event Action? ExitRequested;

    /// <summary>Shows a balloon over the icon. An empty title becomes the program's own name.</summary>
    void Balloon(string title, string text, NotificationKind kind);
}
