using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The few sounds the program makes on its own account: the system switch, and a macro being armed
/// or disarmed. Each is one tone of a fixed pitch, made here rather than taken from the sound
/// scheme, and played the way any other program plays a sound.
/// </summary>
/// <remarks>
/// The tone is synthesised into a WAV and handed to the system's own player. <c>Console.Beep</c> is
/// not used: on Windows it goes through the legacy beep driver, which on most machines has no
/// buzzer behind it and drops most of what is sent, so the same call is silent nine times out of ten
/// and heard once. A WAV through the default output device behaves like every other sound on the
/// machine: the mixer's volume applies, and it starts at once.
/// </remarks>
internal static class AppSound
{
    private const int SampleRate = 44100;

    /// <summary>About a quarter of full scale: clearly audible, not a shout.</summary>
    private const short Amplitude = 8000;

    /// <summary>How long the tone takes to come up and go down, which keeps it from clicking.</summary>
    private const int FadeMs = 5;

    /// <summary>The tone as it goes out. A check hands in its own to read the pitch instead of hearing it.</summary>
    internal static Action<int, int> Beep { get; set; } = PlayTone;

    /// <summary>
    /// Plays the tone for this moment, unless that pair of moments was switched off. A machine that
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

        var (frequency, milliseconds) = UiSounds.Tone(which);
        try
        {
            Beep(frequency, milliseconds);
        }
        catch
        {
            // No output device, an audio service that is not running, a machine that is not
            // Windows: none of these is the click's problem.
        }
    }

    /// <summary>
    /// Builds the tone and starts it. The system player keeps reading the buffer after the call
    /// returns, so each tone's buffer is pinned once and kept: there are four of them, a few
    /// kilobytes each, and a buffer that moved under the player would be a click or nothing at all.
    /// </summary>
    private static void PlayTone(int frequency, int milliseconds)
        => PlaySound(Pinned(frequency, milliseconds), IntPtr.Zero, SndMemory | SndAsync | SndNoDefault);

    /// <summary>One 16-bit mono PCM tone, header and all, as a WAV file in memory.</summary>
    internal static byte[] Wave(int frequency, int milliseconds)
    {
        var samples = Math.Max(1, SampleRate * Math.Max(1, milliseconds) / 1000);
        var fade = Math.Max(1, Math.Min(samples / 4, SampleRate * FadeMs / 1000));
        var data = new byte[samples * 2];

        for (var at = 0; at < samples; at++)
        {
            // Full level in the middle, nothing at either end: a tone that starts mid-air clicks.
            var level = Math.Min(1.0, Math.Min(at, samples - 1 - at) / (double)fade);
            var value = (short)(Amplitude * level * Math.Sin(2 * Math.PI * frequency * at / SampleRate));
            data[at * 2] = (byte)(value & 0xFF);
            data[(at * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        var wave = new byte[44 + data.Length];
        Write(wave, 0, "RIFF");
        Write(wave, 4, 36 + data.Length);
        Write(wave, 8, "WAVE");
        Write(wave, 12, "fmt ");
        Write(wave, 16, 16);              // size of this chunk
        Write(wave, 20, (short)1);        // PCM
        Write(wave, 22, (short)1);        // one channel
        Write(wave, 24, SampleRate);
        Write(wave, 28, SampleRate * 2);  // bytes per second
        Write(wave, 32, (short)2);        // bytes per frame
        Write(wave, 34, (short)16);       // bits per sample
        Write(wave, 36, "data");
        Write(wave, 40, data.Length);
        data.CopyTo(wave, 44);
        return wave;
    }

    /// <summary>The pinned buffer of one tone, made the first time that tone is asked for.</summary>
    private static IntPtr Pinned(int frequency, int milliseconds)
    {
        var key = (frequency, milliseconds);
        lock (PinnedWaves)
        {
            if (!PinnedWaves.TryGetValue(key, out var pointed))
            {
                var handle = GCHandle.Alloc(Wave(frequency, milliseconds), GCHandleType.Pinned);
                pointed = handle.AddrOfPinnedObject();
                PinnedWaves[key] = pointed;
            }

            return pointed;
        }
    }

    private static readonly Dictionary<(int Frequency, int Milliseconds), IntPtr> PinnedWaves = [];

    private static void Write(byte[] wave, int at, string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            wave[at + index] = (byte)text[index];
        }
    }

    private static void Write(byte[] wave, int at, int number)
        => BitConverter.TryWriteBytes(wave.AsSpan(at, 4), number);

    private static void Write(byte[] wave, int at, short number)
        => BitConverter.TryWriteBytes(wave.AsSpan(at, 2), number);

    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    [DllImport("winmm.dll", SetLastError = true)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
}
