using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WhaleGenie.Execution;

/// <summary>
/// What rights this copy of the program was started with, and how to get more of them.
///
/// Windows keeps a program from seeing the keys that go to a window of a higher privilege, and from
/// putting its own input into one — Task Manager, or anything started with "run as administrator".
/// A macro tool reads its hotkeys through a system hook, so this is the one thing that decides
/// whether a hotkey still works while such a window is in front. It is decided at launch and cannot
/// change afterwards, which is why the only way there is a second copy of the program.
/// </summary>
public static class ProcessRights
{
    /// <summary>
    /// Whether this process was started with an administrator token. Read once: a running process
    /// cannot gain or lose that token along the way.
    /// </summary>
    public static bool IsElevated { get; } = Detect();

    /// <summary>
    /// Starts a second copy of this program with the UAC prompt in front of it, and says whether it
    /// got as far as starting one. The caller closes this copy — two of them would be listening for
    /// the same hotkeys, and the next one is not reached by its own process.
    /// </summary>
    public static bool RestartElevated()
    {
        if (IsElevated || Environment.ProcessPath is not { Length: > 0 } program)
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = program,
                UseShellExecute = true,
                Verb = "runas",
            });

            return true;
        }
        catch (Win32Exception)
        {
            // Saying no at the UAC prompt is an answer rather than a failure, and Windows refusing
            // to ask (a policy, a locked-down account) is one too. Either way the copy that is
            // running keeps running.
            return false;
        }
    }

    /// <summary>Answers <see cref="IsElevated"/>, and never fails the program over it.</summary>
    private static bool Detect()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            // No answer means no badge and no suggestion to restart, which is the safe way round.
            return false;
        }
    }
}
