using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>A window rule as both the settings page and the later action editor send it back.</summary>
[BridgeContract]
public sealed record WindowRuleState(
    string Title,
    string Process,
    string ClassName,
    long Handle,
    string How,
    bool CaseSensitive,
    bool Trim,
    bool Multiline);

/// <summary>One labelled choice in a dropdown.</summary>
[BridgeContract]
public sealed record SettingsChoice(string Value, string Label);

/// <summary>The clamps behind every number field in the general settings, so the page does not
/// write its own copy of a range the program already enforces.</summary>
[BridgeContract]
public sealed record SettingsLimits(
    int LogCountMin,
    int LogCountMax,
    int FrameCacheAgeMax,
    double InputRateMin,
    double InputRateMax);

/// <summary>One folder the settings page can open.</summary>
[BridgeContract]
public sealed record SettingsStorage(string Name, string What, string Where, string Folder);

/// <summary>One menu rule row: what it matches, how many pages it has and whether it can grow.</summary>
[BridgeContract]
public sealed record SettingsMenuRule(
    string Key,
    string Summary,
    string PageCountLabel,
    bool IsDefault,
    bool IsBlocked,
    bool CanAddPage,
    WindowRuleState Rule);

/// <summary>The menu launcher and the window rules behind the lower menu region.</summary>
[BridgeContract]
public sealed record SettingsMenuState(
    string Launcher,
    string Display,
    string Status,
    bool IsMiddle,
    bool IsCustom,
    string Problem,
    IReadOnlyList<SettingsMenuRule> Rules,
    bool RememberPinned = false);

/// <summary>One wheel dropdown value, already written as the string the page sends back.</summary>
[BridgeContract]
public sealed record SettingsWheelChoice(string Value, string Label);

/// <summary>One wheel scene and the window rule that selects it.</summary>
[BridgeContract]
public sealed record SettingsWheelScene(
    string Key,
    string Summary,
    bool IsGlobal,
    WindowRuleState Rule);

/// <summary>An icon reference on a wheel slot.</summary>
[BridgeContract]
public sealed record SettingsWheelIcon(string Kind, string Value);

/// <summary>One position of the editor wheel.</summary>
[BridgeContract]
public sealed record SettingsWheelSlot(
    string Ring,
    int Index,
    double X,
    double Y,
    string Position,
    string Macro,
    string Note,
    string Label,
    SettingsWheelIcon? Icon,
    bool Inherited,
    bool Overridden,
    bool Empty,
    string Tone);

/// <summary>The geometry both the desktop wheel and the editor draw from.</summary>
[BridgeContract]
public sealed record SettingsWheelGeometry(
    double Diameter,
    double Center,
    double Inner,
    double Outer,
    double Third,
    double Edge,
    int OuterCount,
    int ThirdCount,
    bool HasOuter,
    bool HasThird);

/// <summary>A macro a wheel slot can point at.</summary>
[BridgeContract]
public sealed record SettingsMacroChoice(string Name, bool Enabled);

/// <summary>The wheel's look, behaviour, scenes and action manager.</summary>
[BridgeContract]
public sealed record SettingsWheelState(
    bool Enabled,
    string Launcher,
    string LauncherStatus,
    string LauncherMessage,
    bool OnlyInPrograms,
    double Diameter,
    double FontSize,
    int ThirdCapacity,
    double TimeoutMs,
    string RepeatKey,
    string RepeatDisplay,
    bool WideOuter,
    bool HideEmptyOuter,
    bool HideLabelWithIcon,
    bool CenterName,
    bool ClampToScreen,
    bool MovePointerToCenter,
    bool ShowGuide,
    bool ExtendedHint,
    bool FollowTheme,
    bool Gloss,
    bool HoverBorder,
    bool PopAnimation,
    int PopAnimationMs,
    bool IsSystemOff,
    string Problem,
    IReadOnlyList<SettingsWheelChoice> LauncherChoices,
    IReadOnlyList<SettingsWheelChoice> ThirdChoices,
    IReadOnlyList<SettingsWheelScene> Scenes,
    string SelectedScene,
    SettingsWheelGeometry Geometry,
    IReadOnlyList<SettingsWheelSlot> Slots,
    IReadOnlyList<SettingsWheelSlot> Preview,
    IReadOnlyList<SettingsMacroChoice> Macros);

/// <summary>The whole settings page, in the shape it draws from.</summary>
[BridgeContract]
public sealed record SettingsState(
    bool LoadLastProjectAtStart,
    bool AutoSaveProject,
    bool SystemAtStart,
    bool EnabledFirst,
    bool IgnoreOwnInput,
    bool SoundSystem,
    bool SoundMacro,
    bool PreloadOcr,
    double InputRate,
    bool FrameRateFollow,
    double FrameRate,
    int FrameRateMax,
    string FrameRateNote,
    bool FrameCache,
    double FrameCacheAge,
    double LogCount,
    string Language,
    string SystemHotkey,
    string RecordStopKey,
    string SystemHotkeyProblem,
    string RecordStopProblem,
    IReadOnlyList<SettingsStorage> Storage,
    string DriverStatus,
    bool DriverReady,
    bool AttachDriverAtStart,
    bool RunAsAdmin,
    string AdminStatus,
    bool CanRestartElevated,
    string RestartNote,
    string WindowWay,
    string ScreenWay,
    IReadOnlyList<SettingsChoice> WindowWays,
    IReadOnlyList<SettingsChoice> ScreenWays,
    SettingsMenuState Menu,
    SettingsWheelState Wheel,
    string PublicMacroEditorSources,
    bool ExportEmbeddedLibrary,
    bool ImportLibraryToProject,
    SettingsLimits Limits);

/// <summary>One open window the rule editor can choose from.</summary>
[BridgeContract]
public sealed record WindowEntryState(
    long Handle,
    string Title,
    string Process,
    string ClassName,
    string Detail,
    string Summary);

/// <summary>The window list and the matching ways a rule may use.</summary>
[BridgeContract]
public sealed record WindowRulePickerState(
    IReadOnlyList<WindowEntryState> Windows,
    IReadOnlyList<SettingsChoice> Hows);
