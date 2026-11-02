using System;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The few sounds the program makes on its own account: the system switch, and a macro being armed
/// or disarmed. Each is one tone of a fixed pitch, made on the machine's speaker, so they do not
/// depend on which sound scheme is installed and stay apart from every other program's sounds.
/// </summary>
internal static class AppSound
{
    /// <summary>
    /// The tone itself, on the machine's speaker. A check hands in its own to read the pitch instead
    /// of hearing it.
    /// </summary>
    internal static Action<int, int> Beep { get; set; } = (frequency, milliseconds) =>
        Console.Beep(frequency, milliseconds);

    /// <summary>
    /// Plays the tone for this moment, unless those two moments were switched off. A tone lasts as
    /// long as it says, and all of them are short: this runs on the thread that handled the click.
    /// A machine that cannot make one is a machine that stays quiet, not a failed click.
    /// </summary>
    public static void Play(UiSound which)
    {
        var on = which is UiSound.SystemOn or UiSound.SystemOff
            ? LocalSettings.LoadSoundSystem()
            : LocalSettings.LoadSoundMacro();

        if (!on)
        {
            return;
        }

        var (frequency, milliseconds) = UiSounds.Tone(which);
        try
        {
            Beep(frequency, milliseconds);
        }
        catch
        {
            // No speaker, an audio service that is not running, a machine that is not Windows: the
            // switch itself worked, and that is what the click was for.
        }
    }
}
