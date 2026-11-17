using System.Threading;

namespace WhaleGenie.Runtime;

/// <summary>
/// Holds the main window's triggers back while a dialog of WhaleGenie's own is open.
/// </summary>
/// <remarks>
/// Editing a macro means typing, and typing means pressing keys — including, while a shortcut is
/// being set, the very key a macro waits for. A dialog that counts itself here keeps that from
/// setting anything off.
/// </remarks>
public static class MacroTriggerGate
{
    private static int _open;

    /// <summary>True while a dialog that should hold the triggers back is open.</summary>
    public static bool IsOpen => Volatile.Read(ref _open) > 0;

    /// <summary>Counts a dialog as open. Every call has to be matched by <see cref="Exit"/>.</summary>
    public static void Enter() => Interlocked.Increment(ref _open);

    /// <summary>Counts a dialog as closed.</summary>
    public static void Exit() => Interlocked.Decrement(ref _open);

    /// <summary>
    /// Starts from "no dialog open". Each check runs a fresh application, so a count left behind
    /// by a window that check opened and never closed belongs to an application that is gone; the
    /// next check must not inherit it and find every floating panel held back.
    /// </summary>
    internal static void ResetForChecks() => Volatile.Write(ref _open, 0);
}
