namespace WhaleGenie.Bridge;

/// <summary>The stable rejection words the bridge answers with.</summary>
public static class BridgeErrors
{
    public const string BadArgument = "BadArgument";
    public const string NoSuchMethod = "NoSuchMethod";
    public const string NoSuchMacro = "NoSuchMacro";

    /// <summary>
    /// A close that refused to discard unsaved work. The page reads this as "ask the person",
    /// not as an error to put in a banner; the code is separate so it cannot be mistaken for a
    /// malformed command.
    /// </summary>
    public const string EditorUnsaved = "Editor.Unsaved";
    public const string PackageOpenFailed = "PackageOpenFailed";
    public const string PackageSaveFailed = "PackageSaveFailed";
    public const string Internal = "Internal";
}
