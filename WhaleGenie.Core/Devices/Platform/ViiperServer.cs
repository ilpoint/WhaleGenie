using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The VIIPER server that the virtual keyboard and mouse live in.
///
/// It belongs to another project, but unlike the usbip-win2 driver — which is installed once and
/// belongs to the machine — it is only a program. That is what makes "start the server first"
/// something WhaleGenie can do instead of asking the user for: a macro that needs driver-level
/// input works whether or not anybody launched the server by hand. A server that was already there
/// is left alone, here and at the end: whoever started it owns it.
/// </summary>
public static class ViiperServer
{
    /// <summary>
    /// The flag a client on this machine needs. Without it the devices go on the bus and never
    /// reach the desktop, so every launch carries it rather than depending on how the user ran it.
    /// </summary>
    public const string AutoAttach = "--api.auto-attach-local-client";

    /// <summary>
    /// How long a start waits for the server to answer. The program is a few megabytes and starts a
    /// USB bus, so this is generous; giving up only means the run reports what is missing.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(8);

    /// <summary>How often the start looks whether the server is up yet.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(150);

    private static readonly object Gate = new();

    /// <summary>The server this program started, and the only one it may stop again.</summary>
    private static Process? _started;

    /// <summary>
    /// Whether the program is on its way out. A check sets it, because what it decides — whether a
    /// start that is still being waited for is cut short — is worth reading without a program
    /// actually ending; the way out is the only thing that sets it for real.
    /// </summary>
    internal static bool Leaving
    {
        get => _leaving;
        set => _leaving = value;
    }

    /// <summary>
    /// Read from the warm-up's own thread while the way out runs on the interface thread, so it is
    /// a field that is read and written whole rather than a plain one.
    /// </summary>
    private static volatile bool _leaving;

    /// <summary>
    /// The job the servers this program starts are put in, or zero when the machine would not give
    /// us one. The handle is kept for the life of the program on purpose: closing it is what takes
    /// the job's processes down, and that is the whole point of having it.
    /// </summary>
    private static IntPtr _job;

    /// <summary>
    /// Whether a server is answering. A check hands in an answer of its own, so what happens when
    /// one is or is not there can be read without a server on the machine.
    /// </summary>
    internal static Func<bool> Answering { get; set; } = DriverInput.IsServerAnswering;

    /// <summary>
    /// Whether the usbip-win2 driver is on this machine, which is what makes a server worth
    /// starting. A check hands in its own answer, like the two above.
    /// </summary>
    internal static Func<bool> DriverInstalled { get; set; }
        = () => DriverInput.Check() != DriverInputState.DriverMissing;

    /// <summary>
    /// How the program is put on the machine and waited for. A check hands in something that records
    /// what would have been started instead of starting it.
    /// </summary>
    internal static Func<string, bool> Launch { get; set; } = StartProcess;

    /// <summary>
    /// Where viiper.exe is, as the settings window was told. WhaleGenie does not go looking for it:
    /// the user unzips that project's download wherever they like and points at the program once.
    /// Null means nobody has said, which is the case until the settings window is used.
    /// </summary>
    public static string? Executable { get; set; }

    /// <summary>
    /// Starts what gets started before anybody needs it, on a thread of its own so the window that
    /// asked for it does not wait.
    ///
    /// Starting that program and its USB bus costs a second and a half on this machine, and it is
    /// paid by whichever macro step is the first to ask — so a macro whose first move is
    /// driver-level would sit there doing nothing for that long and look broken. Warming up when
    /// the program starts moves that cost to where nobody is waiting. It is only an optimisation:
    /// everything is still started on demand when this was not done or did not work.
    /// </summary>
    public static Task WarmUp() => Task.Run(() =>
    {
        try
        {
            // Nothing to warm up without the driver, and nothing to do when a server is already
            // answering — that one is not ours to touch. And nothing at all once the program is on
            // its way out: a server started now would be one nothing is left to close.
            if (Leaving || Answering() || !DriverInstalled())
            {
                return;
            }

            Ensure();
        }
        catch
        {
            // Whatever is missing is reported by the step that needs it, with a message that says
            // what to do about it. A warm-up that fails is not worth telling anybody about.
        }
    });

    /// <summary>
    /// Makes sure a server is answering, starting the one at <see cref="Executable"/> when nothing
    /// is. False means driver-level input has no server to talk to and nothing could be started.
    /// </summary>
    public static bool Ensure()
    {
        if (Answering())
        {
            return true;
        }

        lock (Gate)
        {
            // The look above and this one are both outside the lock, so two runs asking at the same
            // moment do not both start a server: the second one finds the first one's.
            if (Answering())
            {
                return true;
            }

            // A server started from here would be one this program has to be able to close again,
            // and on the way out it cannot: nothing would be left to do it. A server that is
            // already answering is a different matter, and was let through above.
            if (Leaving)
            {
                return false;
            }

            if (Executable is not { Length: > 0 } path || !File.Exists(path))
            {
                return false;
            }

            return Launch(path);
        }
    }

    /// <summary>
    /// Stops the server this program started, and stops this program from starting another: it is on
    /// its way out, and a server started in the last moment would be one nothing would be left to
    /// close. A server it did not start is left running, and so is one that is answering after ours
    /// was closed: something else is using it.
    /// </summary>
    public static void Stop()
    {
        // Before the lock, so that a start which is waiting inside it gives up on its next look and
        // one that has not begun yet does not begin at all.
        Leaving = true;
        Close();
    }

    /// <summary>
    /// Stops the server this program started for a hand-over: the copy taking over starts its own,
    /// and one left over from this copy would be closed on the way out, right under the new one.
    /// This is not the end of the program, though — the hand-over can be called off, and then this
    /// copy is still the one running — so starting a server again stays allowed.
    /// </summary>
    public static void HandOver() => Close();

    private static void Close()
    {
        Process? started;
        lock (Gate)
        {
            started = _started;
            _started = null;
        }

        if (started is null)
        {
            return;
        }

        try
        {
            if (!started.HasExited)
            {
                started.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // A server that is already gone, or that will not go, is not something to report on the
            // way out of the program.
        }
        finally
        {
            started.Dispose();
        }
    }

    /// <summary>Puts the server on the machine and waits for it to answer. Called under the lock.</summary>
    private static bool StartProcess(string path)
    {
        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = path,
                Arguments = AutoAttach,
                WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
        }
        catch
        {
            // A file that is not a program, or one Windows refuses to run, is the same answer as no
            // server: the run reports what is missing.
            return false;
        }

        KeepWithUs(process);

        var waiting = Stopwatch.StartNew();
        while (waiting.Elapsed < Patience)
        {
            // The program can begin leaving while this is still waiting for the server to answer.
            // A server that arrives after that is one nothing is left to close, so this one is
            // closed again with the rest of the failure path below instead of being written down.
            if (Leaving)
            {
                break;
            }

            if (Answering())
            {
                _started = process;
                return true;
            }

            if (process.HasExited)
            {
                break;
            }

            Thread.Sleep(Interval);
        }

        // A server that never answered is closed again: leaving a program nobody can talk to running
        // behind the user's back is worse than reporting the failure.
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // As above: closing it is best effort.
        }

        process.Dispose();
        return false;
    }

    /// <summary>
    /// Puts a server into a job object that dies with this program, so Windows takes it down with us
    /// even where nothing in this program is given the chance to: a crash, or a program ended from
    /// the task manager. A server that outlives us is one the next launch never started and so never
    /// closes, which is exactly how one ends up running with nothing to own it.
    /// </summary>
    /// <remarks>
    /// Best effort: a machine that will not hand out a job leaves everything as it was, and
    /// <see cref="Stop"/> still closes the server the ordinary way. Called under the lock.
    /// </remarks>
    private static void KeepWithUs(Process process)
    {
        try
        {
            if (_job == IntPtr.Zero)
            {
                _job = CreateJobObjectW(IntPtr.Zero, null);
            }

            if (_job == IntPtr.Zero)
            {
                return;
            }

            var limits = new JobObjectExtendedLimitInformation
            {
                BasicLimitInformation = new JobObjectBasicLimitInformation
                {
                    // "Closing the last handle to this job kills everything in it", which is the
                    // one limit that means the server goes when this program does.
                    LimitFlags = KillOnJobClose,
                },
            };

            if (!SetInformationJobObject(_job, JobObjectExtendedLimitInformationClass, ref limits,
                    (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
            {
                return;
            }

            AssignProcessToJobObject(_job, process.Handle);
        }
        catch
        {
            // Windows refusing the job is the same as not having one.
        }
    }

    /// <summary>Closing the last handle to the job closes everything that was put in it.</summary>
    private const uint KillOnJobClose = 0x2000;

    /// <summary>The information class that carries the extended limits, including the one above.</summary>
    private const int JobObjectExtendedLimitInformationClass = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass,
        ref JobObjectExtendedLimitInformation information, uint length);

    [DllImport("kernel32.dll")]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
}
