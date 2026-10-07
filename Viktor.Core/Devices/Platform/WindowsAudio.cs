using System;
using System.Runtime.InteropServices;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// The volume of the speakers Windows is using, through the same Core Audio interface the taskbar's
/// volume slider talks to. This is the only way to change the machine's volume rather than a
/// window's: pressing the keyboard's volume keys changes whatever has the focus, and there may be
/// nothing there at all.
/// </summary>
internal static class WindowsAudio
{
    /// <summary>The volume of the default speakers, from 0 to 100.</summary>
    public static int Volume()
    {
        var endpoint = Endpoint();
        Asked(endpoint.GetMasterVolumeLevelScalar(out var level));

        // The interface hands back a fraction of the way up the slider, and a macro wants the number
        // the taskbar shows.
        return (int)Math.Round(level * 100);
    }

    public static void SetVolume(int percent)
    {
        var endpoint = Endpoint();
        var level = Math.Clamp(percent, 0, 100) / 100f;
        Asked(endpoint.SetMasterVolumeLevelScalar(level, ref NoContext));
    }

    public static bool Muted()
    {
        var endpoint = Endpoint();
        Asked(endpoint.GetMute(out var muted));
        return muted != 0;
    }

    public static void SetMuted(bool muted)
    {
        var endpoint = Endpoint();
        Asked(endpoint.SetMute(muted ? 1 : 0, ref NoContext));
    }

    /// <summary>
    /// Plays one of the machine's own event sounds. The sound is the one the user's sound scheme
    /// gives that event, so it is what they expect to hear. A machine told to keep quiet plays
    /// nothing and says nothing about it, but one that has no sound device at all says so here
    /// rather than leaving a macro that meant to be heard quietly unheard.
    /// </summary>
    public static void Play(SoundKind kind)
    {
        var played = MessageBeep(kind switch
        {
            SoundKind.Information => Notice,
            SoundKind.Warning => Caution,
            SoundKind.Error => Stop,
            SoundKind.Question => Ask,
            _ => Any,
        });

        if (!played)
        {
            throw new DeviceActionException("Run.SoundRefused");
        }
    }

    /// <summary>
    /// The speakers Windows is currently playing through. A machine with no sound card, or one whose
    /// audio service is not running, has none of these, and says so rather than reporting a volume
    /// of zero.
    /// </summary>
    private static IAudioEndpointVolume Endpoint()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("the sound card");
        }

        try
        {
            var devices = (IMMDeviceEnumerator)new MMDeviceEnumerator();

            // eRender is the speakers rather than the microphone, and eMultimedia is what the
            // machine's own sounds go through, which is the one the volume slider moves.
            Asked(devices.GetDefaultAudioEndpoint(Render, Multimedia, out var device));

            var wanted = typeof(IAudioEndpointVolume).GUID;
            Asked(device.Activate(ref wanted, InProcess, IntPtr.Zero, out var volume));
            return volume;
        }
        catch (Exception error) when (error is COMException or InvalidCastException
            or EntryPointNotFoundException or PlatformNotSupportedException)
        {
            throw new DeviceUnavailableException("the sound card");
        }
    }

    /// <summary>
    /// Turns a Windows answer code into a failure the interface can word. Anything from zero upwards
    /// is a success: asking for the sound to be switched off when it is already off comes back as
    /// one — "there was nothing to do" — and a step that failed over that would be failing because
    /// what it asked for was already true. Windows was measured saying exactly that.
    /// </summary>
    private static void Asked(int answer)
    {
        if (answer < 0)
        {
            throw new DeviceActionException("Run.AudioRefused", $"0x{answer:X8}");
        }
    }

    /// <summary>Nothing to report back, which is what a call that does not care sends.</summary>
    private static Guid NoContext;

    private const int Render = 0;
    private const int Multimedia = 1;
    private const uint InProcess = 1;

    /// <summary>The list of audio devices Windows has, and which of them is the default.</summary>
    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);

        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);

        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);

        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }

    /// <summary>One audio device, which is asked for the control of its own volume.</summary>
    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid id, uint context, IntPtr parameters,
            [MarshalAs(UnmanagedType.Interface)] out IAudioEndpointVolume volume);

        [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);

        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

        [PreserveSig] int GetState(out uint state);
    }

    /// <summary>
    /// The volume of one device. Every entry is written out in the order Windows declares them,
    /// because that order is the only thing a COM interface has: leaving one out would make every
    /// call after it ask for the wrong thing.
    /// </summary>
    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);

        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);

        [PreserveSig] int GetChannelCount(out uint channels);

        [PreserveSig] int SetMasterVolumeLevel(float decibels, ref Guid context);

        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);

        [PreserveSig] int GetMasterVolumeLevel(out float decibels);

        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);

        [PreserveSig] int SetChannelVolumeLevel(uint channel, float decibels, ref Guid context);

        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float decibels);

        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);

        [PreserveSig] int SetMute(int muted, ref Guid context);

        [PreserveSig] int GetMute(out int muted);
    }

    /// <summary>The one class that hands out the audio device list.</summary>
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumerator
    {
    }

    /// <summary>
    /// Plays the sound Windows keeps for one kind of event. The four kinds are the ones the sound
    /// settings window lists by name; 0xFFFFFFFF asks for whatever the machine plays when nothing
    /// more particular is wanted.
    /// </summary>
    [DllImport("user32.dll")]
    private static extern bool MessageBeep(uint kind);

    private const uint Any = 0xFFFFFFFF;
    private const uint Stop = 0x00000010;
    private const uint Ask = 0x00000020;
    private const uint Caution = 0x00000030;
    private const uint Notice = 0x00000040;
}
