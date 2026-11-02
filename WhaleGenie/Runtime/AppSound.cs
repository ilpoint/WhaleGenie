using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The few sounds the program makes on its own account: the system switch and a macro being armed or
/// disarmed. They are read from the settings window rather than kept here, so what is heard is what
/// the person chose, and they are played through the same system device a macro's own sound uses.
/// </summary>
internal static class AppSound
{
    /// <summary>
    /// Plays the sound that moment is set to, unless the interface was told to keep quiet. A machine
    /// with no sound device refuses; that refusal must not turn "arm this macro" into a failure, so
    /// it is swallowed here rather than reaching the click that asked for the sound.
    /// </summary>
    public static void Play(ISystemDevice system, UiSound which)
    {
        if (!LocalSettings.LoadSounds())
        {
            return;
        }

        try
        {
            system.PlaySound(LocalSettings.LoadSound(which));
        }
        catch
        {
            // A missing sound card, an audio service that is not running, or a device that refuses:
            // none of these is worth a message, and all of them happen on real machines.
        }
    }
}
