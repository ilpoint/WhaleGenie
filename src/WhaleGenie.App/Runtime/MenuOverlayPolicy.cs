namespace WhaleGenie.Runtime;

/// <summary>
/// When a menu overlay goes away by itself: another program took the foreground and the person did
/// not pin it. The overlay's own window becoming the foreground is not "another program".
/// </summary>
public static class MenuOverlayPolicy
{
    public static bool ShouldClose(bool visible, bool pinned, long ownHandle, long foregroundHandle)
        => visible
           && !pinned
           && foregroundHandle != 0
           && foregroundHandle != ownHandle;
}
