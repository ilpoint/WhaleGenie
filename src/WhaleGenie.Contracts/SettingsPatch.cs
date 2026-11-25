namespace WhaleGenie.Contracts;

/// <summary>
/// One edit of the settings page. Every field is optional: the page sends only what changed, and
/// the program validates, clamps and stores it before answering with the new state.
/// </summary>
[BridgeContract]
[BridgePatch]
public sealed record SettingsPatch(
    bool? LoadLastProjectAtStart,
    bool? AutoSaveProject,
    bool? SystemAtStart,
    bool? EnabledFirst,
    bool? IgnoreOwnInput,
    bool? SoundSystem,
    bool? SoundMacro,
    bool? PreloadOcr,
    bool? FrameRateFollow,
    double? InputRate,
    double? FrameRate,
    double? FrameCacheAge,
    double? LogCount,
    bool? FrameCache,
    bool? AttachDriverAtStart,
    bool? RunAsAdmin,
    string? WindowWay,
    string? ScreenWay,
    string? Language,
    string? SystemHotkey,
    string? RecordStopKey);
