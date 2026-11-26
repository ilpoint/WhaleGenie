using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGet.Common;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Platform.Windows;

/// <summary>
/// The scripts this program runs itself, and the packages they ask for. A script of one of these
/// languages runs in a process of its own with a way back into the macro over a local pipe, so it
/// can read and write the macro's values and run its steps while it runs — and whatever it does to
/// itself, a crash or an exit of its own or a loop that never ends, touches nothing of the program
/// that started it.
/// </summary>
/// <remarks>
/// The pipe is served on the thread that asked for the run, which is the thread walking the macro:
/// a script that asks for a step to be run gets it run there and then, the way a block inside a
/// macro runs the steps inside it. The three streams the child has — stdout, stderr and the pipe —
/// are each read on a thread of their own and handed over in the order they arrived.
/// </remarks>
public sealed partial class ScriptDevice : IScriptDevice
{
    /// <summary>How long the program waits for a script to go away after it has been told to stop.</summary>
    private const int StopWaitMs = 2000;

    private readonly IProcessDevice _processes;
    private readonly string _packages;
    private readonly string _host;

    /// <summary>
    /// The device the program itself uses: packages beside the program, this machine's own programs,
    /// and this program's own file to run a compiled script with.
    /// </summary>
    public ScriptDevice(IProcessDevice processes)
        : this(processes, ScriptFolders.Packages, Environment.ProcessPath ?? string.Empty)
    {
    }

    /// <summary>The same with somewhere else to keep the packages and another file to run scripts
    /// with, which is what a check hands in.</summary>
    public ScriptDevice(IProcessDevice processes, string packages, string host)
    {
        _processes = processes;
        _packages = packages;
        _host = host;
    }

    /// <summary>This machine's AutoHotkey, or null when the program is not installed here.</summary>
    public string? AutoHotkeyProgram => AutoHotkey.Program();

    /// <summary>Where the Python packages live: the folder every Python script imports from.</summary>
    private string PythonPackages => Path.Combine(_packages, "python");

    /// <summary>Where the NuGet packages live: the folder every C# script is compiled against.</summary>
    private string NuGetPackages => Path.Combine(_packages, "nuget");
}
