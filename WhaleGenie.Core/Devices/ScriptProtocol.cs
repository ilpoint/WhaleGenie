namespace WhaleGenie.Core.Devices;

/// <summary>
/// The three names the macro side and the program it starts to run a script both spell: the
/// argument that turns the program into a script host, and the two environment variables that tell
/// that second process where to answer and where its packages live. They are here rather than
/// beside the host because the host is a Windows process and the pipe is the engine's contract.
/// </summary>
public static class ScriptProtocol
{
    /// <summary>The environment variable the child is told the pipe by.</summary>
    public const string PipeVariable = "WHALEGENIE_PIPE";

    /// <summary>The environment variable that tells the child where this run keeps script packages.</summary>
    public const string PackagesVariable = "WHALEGENIE_PACKAGES";

    /// <summary>What the program is asked with when it is being run as a script rather than as itself.</summary>
    public const string Flag = "--script-host";
}
