namespace WhaleGenie.Bridge;

/// <summary>Which page a message belongs to: the shell, or one of the overlays beside it.</summary>
public enum BridgeSurface
{
    /// <summary>The shell: project, editor, settings and run panels.</summary>
    Main,

    /// <summary>The macro menu overlay.</summary>
    Menu,

    /// <summary>The wheel overlay.</summary>
    Wheel,

    /// <summary>One interface panel beside the window in front.</summary>
    Panel,

    /// <summary>The separate macro workbench: the editor and the run/debug page.</summary>
    Workbench,
}

/// <summary>How a push treats an older push of the same event that has not gone out yet.</summary>
public enum BridgePushMode
{
    /// <summary>Replace the queued payload with the newest one; the page only needs the latest state.</summary>
    Latest,

    /// <summary>Keep every push, in order — log lines and other streams need each one.</summary>
    All,
}

/// <summary>Names the protocol's fixed words in one place.</summary>
public static class BridgeProtocol
{
    /// <summary>The page sends this once its receiver is registered; buffered events follow.</summary>
    public const string ReadyMethod = "bridge.ready";
}
