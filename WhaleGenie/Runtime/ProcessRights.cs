using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Principal;
using WhaleGenie.Localization;

namespace WhaleGenie.Runtime;

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
    /// The word this program passes to the copy it started with the administrator token. A copy that
    /// sees it knows it is the answer to that request and does not ask again — otherwise a refused
    /// UAC prompt would leave every start trying to start another one.
    /// </summary>
    public const string ElevatedFlag = "--elevated";

    /// <summary>
    /// Starts an elevated copy when the settings asked for one and this copy does not have those
    /// rights. True means that copy is on its way and this one should end: two of them would be
    /// listening for the same hotkeys.
    /// </summary>
    public static bool AutoRestart(IReadOnlyList<string> args)
        => WantsElevation(IsElevated, LocalSettings.LoadRunAsAdmin(), args)
            && RestartElevated(ElevatedFlag);

    /// <summary>
    /// Whether this start should hand itself over to an elevated one. Kept on its own so the four
    /// answers — already elevated, not asked for, answered already, and yes — can be checked without
    /// changing what this machine's token is.
    /// </summary>
    internal static bool WantsElevation(bool elevated, bool asked, IReadOnlyList<string> args)
        => !elevated
           && asked
           && !args.Any(argument =>
               argument.Equals(ElevatedFlag, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Starts a second copy of this program with the UAC prompt in front of it, and says whether it
    /// got as far as starting one. The caller closes this copy — two of them would be listening for
    /// the same hotkeys, and the next one is not reached by its own process.
    /// </summary>
    public static bool RestartElevated(params string[] args)
    {
        if (IsElevated || Environment.ProcessPath is not { Length: > 0 } program)
        {
            return false;
        }

        try
        {
            var start = new ProcessStartInfo
            {
                FileName = program,
                UseShellExecute = true,
                Verb = "runas",
            };

            if (args.Length > 0)
            {
                // ArgumentList cannot be handed to the shell; the words are this program's own and
                // none of them holds a space.
                start.Arguments = string.Join(' ', args);
            }

            Process.Start(start);

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
