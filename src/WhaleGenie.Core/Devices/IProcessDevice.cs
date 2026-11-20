using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices;

// Programs and commands, and the details read off one.
/// <summary>Other programs: starting them, watching them, and stopping them.</summary>
public interface IProcessDevice
{
    /// <summary>Starts a program and hands back its process id.</summary>
    int Start(StartRequest request);

    /// <summary>
    /// The ids of the running processes whose name matches, compared the way the step says. A name
    /// is asked for several times a second by a step that waits for one to appear, so an exact
    /// name that does not care about case is answered from Windows' own listing rather than by
    /// reading every process on the machine; anything else is matched here, name by name.
    /// </summary>
    IReadOnlyList<int> Find(string value, string compare = TextCompare.Exactly,
        TextCompareOptions options = default);

    /// <summary>Every running process, by name, without repeats.</summary>
    IReadOnlyList<string> List();

    /// <summary>Whether the process with this id has finished.</summary>
    bool HasExited(int id);

    /// <summary>What a process returned, or null while it is still running.</summary>
    int? ExitCode(int id);

    /// <summary>Stops one process by id.</summary>
    bool StopById(int id, bool force);

    /// <summary>Runs a program to the end and collects what it printed.</summary>
    CommandResult Run(CommandRequest request);

    /// <summary>What is known about one running program, or null when that id is not running.</summary>
    ProcessDetails? Details(int id);
}

/// <summary>
/// One running program as the macro can see it. <paramref name="Path"/> is empty when Windows
/// will not say — a program running as another user or at a higher level keeps its file to itself.
/// The main window handle and title are zero and empty while the program has no main window yet,
/// which is ordinary for a console program and for a window that is still starting up.
/// </summary>
public sealed record ProcessDetails(
    int Id,
    string Name,
    string Path,
    double MemoryMb,
    double CpuSeconds,
    long MainWindowHandle = 0,
    string MainWindowTitle = "");
