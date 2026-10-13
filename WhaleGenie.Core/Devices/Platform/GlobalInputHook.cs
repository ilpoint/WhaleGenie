namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The one keyboard and mouse hook this process installs, handed out as what it can say rather
/// than as the library that says it.
/// </summary>
/// <remarks>
/// Windows does not hand a global hook over cleanly twice: a hook made after an earlier one has
/// been taken down stops receiving events, so a recorder that built its own hook worked for the
/// first couple of recordings and then quietly caught nothing. The hook is therefore installed
/// once and kept for the life of the process, and whatever needs it subscribes while it works.
/// </remarks>
public static class GlobalInputHook
{
    private static readonly object Gate = new();
    private static SharpHookInputHook? _shared;

    /// <summary>
    /// The hook, installed the first time anything asks for it; a hook that has died is replaced,
    /// so a later recording can still work. Throws when the hook cannot be installed at all.
    /// </summary>
    public static IInputHook Shared
    {
        get
        {
            lock (Gate)
            {
                if (_shared is { IsAlive: true } alive)
                {
                    return alive;
                }

                _shared = new SharpHookInputHook();
                return _shared;
            }
        }
    }
}
