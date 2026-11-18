using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The few sounds the program makes on its own account: the system switch, and a macro being armed
/// or disarmed. Each is a short WAV packed beside the program, played through the machine's default
/// output device so it behaves like every other sound on the machine.
/// </summary>
/// <remarks>
/// The four are different sounds, and the two pairs are told apart by ear: the program's own switch
/// and one macro never sound alike. They are read out of the program's resources rather than from
/// files on disk, so nothing can go missing after an update or a move.
/// </remarks>
internal static class AppSound
{
    /// <summary>The file behind each moment, packed into the assembly.</summary>
    private static readonly Dictionary<UiSound, string> Files = new()
    {
        [UiSound.SystemOn] = "notification_pluck_on.wav",
        [UiSound.SystemOff] = "notification_off.wav",
        [UiSound.MacroOn] = "pop_on.wav",
        [UiSound.MacroOff] = "pop_off.wav",
    };

    private static readonly Dictionary<UiSound, byte[]> Loaded = [];
    private static readonly Dictionary<UiSound, IntPtr> Pointed = [];

    /// <summary>
    /// Where a moment's sound goes out. A check hands in its own to see which sound was asked for
    /// instead of hearing it.
    /// </summary>
    internal static Action<UiSound> PlayTone { get; set; } = Start;

    /// <summary>
    /// Plays the sound for this moment, unless that pair of moments was switched off. A machine that
    /// cannot make a sound stays quiet rather than failing the click that asked for it.
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

        try
        {
            PlayTone(which);
        }
        catch
        {
            // No output device, an audio service that is not running, a machine that is not
            // Windows: none of these is the click's problem.
        }
    }

    /// <summary>The bytes of this moment's sound, read out of the program's resources once.</summary>
    internal static byte[] Wave(UiSound which)
    {
        lock (Loaded)
        {
            if (!Loaded.TryGetValue(which, out var wave))
            {
                using var stream = typeof(AppSound).Assembly.GetManifestResourceStream(
                    $"WhaleGenie.Sounds.{Files[which]}")
                    ?? throw new InvalidOperationException(
                        $"The sound {Files[which]} is not packed into the program.");
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                wave = memory.ToArray();
                Loaded[which] = wave;
            }

            return wave;
        }
    }

    /// <summary>
    /// Starts the sound. The player keeps reading the buffer after the call returns, so each one is
    /// pinned once and kept: there are four of them, and a buffer that moved under the player would
    /// be a click or nothing at all.
    /// </summary>
    private static void Start(UiSound which)
    {
        IntPtr pointed;
        lock (Pointed)
        {
            if (!Pointed.TryGetValue(which, out pointed))
            {
                pointed = GCHandle.Alloc(Wave(which), GCHandleType.Pinned).AddrOfPinnedObject();
                Pointed[which] = pointed;
            }
        }

        Win32.PlaySound(pointed, IntPtr.Zero,
            Win32.SndMemory | Win32.SndAsync | Win32.SndNoDefault);
    }

    /// <summary>The one call this class needs from Windows.</summary>
    private static class Win32
    {
        public const uint SndAsync = 0x0001;
        public const uint SndNoDefault = 0x0002;
        public const uint SndMemory = 0x0004;

        [DllImport("winmm.dll", SetLastError = true)]
        public static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
    }
}
