using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The three vocabularies a run writes: failure keys the macro can branch on, log keys that word a
/// line of the run log, and the status keys a whole run ends with. Keeping them apart is what makes
/// <c>step.&lt;id&gt;.error</c> a stable answer rather than a sentence.
/// </summary>
public static class RunVocabulary
{
    /// <summary>Every failure key the engine can report.</summary>
    public static IReadOnlyList<string> Failures => RunKeys.All;

    /// <summary>Every key a run log line can carry.</summary>
    public static IReadOnlyList<string> Logs => LogKeys.All;

    /// <summary>Every status a whole run can end with.</summary>
    public static IReadOnlyList<string> Statuses => StatusKeys.All;
}

/// <summary>
/// Every failure key a step can report. The value is what a macro branches on in
/// <c>step.&lt;id&gt;.error</c>; a log line never borrows one of these.
/// </summary>
public static class RunKeys
{
    public const string AbortRefused = "Run.AbortRefused";
    public const string AhkNeedsV2 = "Run.AhkNeedsV2";
    public const string AlignNeedsNames = "Run.AlignNeedsNames";
    public const string AudioRefused = "Run.AudioRefused";
    public const string BackgroundNeedsPointer = "Run.BackgroundNeedsPointer";
    public const string BadArgumentType = "Run.BadArgumentType";
    public const string BadBase64 = "Run.BadBase64";
    public const string BadEnvironment = "Run.BadEnvironment";
    public const string BadExpression = "Run.BadExpression";
    public const string BadDeclaration = "Run.BadDeclaration";
    public const string BadJson = "Run.BadJson";
    public const string BadJsonPath = "Run.BadJsonPath";
    public const string BadList = "Run.BadList";
    public const string BadMatchMode = "Run.BadMatchMode";
    public const string BadModel = "Run.BadModel";
    public const string BadModelShape = "Run.BadModelShape";
    public const string BadOption = "Run.BadOption";
    public const string BadPattern = "Run.BadPattern";
    public const string BadPoint = "Run.BadPoint";
    public const string BadPoints = "Run.BadPoints";
    public const string BadPosition = "Run.BadPosition";
    public const string BadProcessId = "Run.BadProcessId";
    public const string BadRegion = "Run.BadRegion";
    public const string BadResultType = "Run.BadResultType";
    public const string BadZip = "Run.BadZip";
    public const string BreakOutsideLoop = "Run.BreakOutsideLoop";
    public const string BrightnessRefused = "Run.BrightnessRefused";
    public const string BrowserBadFilter = "Run.BrowserBadFilter";
    public const string BrowserBadProfile = "Run.BrowserBadProfile";
    public const string BrowserFailed = "Run.BrowserFailed";
    public const string BrowserMissing = "Run.BrowserMissing";
    public const string BrowserNoAttribute = "Run.BrowserNoAttribute";
    public const string BrowserNoDialog = "Run.BrowserNoDialog";
    public const string BrowserNoNewTab = "Run.BrowserNoNewTab";
    public const string BrowserRunWhileHeld = "Run.BrowserRunWhileHeld";
    public const string BrowserNoScript = "Run.BrowserNoScript";
    public const string BrowserNoSelector = "Run.BrowserNoSelector";
    public const string BrowserNoTab = "Run.BrowserNoTab";
    public const string BrowserNotFound = "Run.BrowserNotFound";
    public const string BrowserNotOpen = "Run.BrowserNotOpen";
    public const string BrowserTabNeedsText = "Run.BrowserTabNeedsText";
    public const string BrowserTooFew = "Run.BrowserTooFew";
    public const string CaptureFailed = "Run.CaptureFailed";
    public const string ChildStillRunning = "Run.ChildStillRunning";
    public const string ChildStopped = "Run.ChildStopped";
    public const string ClipboardBusy = "Run.ClipboardBusy";
    public const string ClipboardTimeout = "Run.ClipboardTimeout";
    public const string ClipboardWriteFailed = "Run.ClipboardWriteFailed";
    public const string CommandTimeout = "Run.CommandTimeout";
    public const string ContinueOutsideLoop = "Run.ContinueOutsideLoop";
    public const string DetectNotFound = "Run.DetectNotFound";
    public const string DeviceFailed = "Run.DeviceFailed";
    public const string ElementNotATable = "Run.ElementNotATable";
    public const string ElementNotCheckable = "Run.ElementNotCheckable";
    public const string ElementNotClickable = "Run.ElementNotClickable";
    public const string ElementNotExpandable = "Run.ElementNotExpandable";
    public const string ElementNotFound = "Run.ElementNotFound";
    public const string ElementNotInvocable = "Run.ElementNotInvocable";
    public const string ElementNotScrollable = "Run.ElementNotScrollable";
    public const string ElementNotSelectable = "Run.ElementNotSelectable";
    public const string ElementNotWritable = "Run.ElementNotWritable";
    public const string ElevationNoEnvironment = "Run.ElevationNoEnvironment";
    public const string ElevationRefused = "Run.ElevationRefused";
    public const string EmptyChord = "Run.EmptyChord";
    public const string EmptyRegion = "Run.EmptyRegion";
    public const string EmptySelector = "Run.EmptySelector";
    public const string Failed = "Run.Failed";
    public const string FileFailed = "Run.FileFailed";
    public const string FileNotFound = "Run.FileNotFound";
    public const string FolderNotFound = "Run.FolderNotFound";
    public const string GlobalMustExist = "Run.GlobalMustExist";
    public const string ImageNotFound = "Run.ImageNotFound";
    public const string IndexOutOfRange = "Run.IndexOutOfRange";
    public const string ItemNotFound = "Run.ItemNotFound";
    public const string LayoutNotFound = "Run.LayoutNotFound";
    public const string LayoutNotSwitched = "Run.LayoutNotSwitched";
    public const string LockRefused = "Run.LockRefused";
    public const string MacroNotFound = "Run.MacroNotFound";
    public const string MacroTooDeep = "Run.MacroTooDeep";
    public const string MissingArgument = "Run.MissingArgument";
    public const string MissingCondition = "Run.MissingCondition";
    public const string MissingField = "Run.MissingField";
    public const string MissingHandle = "Run.MissingHandle";
    public const string MissingImage = "Run.MissingImage";
    public const string MissingInterpreter = "Run.MissingInterpreter";
    public const string MissingItem = "Run.MissingItem";
    public const string MissingJsonPath = "Run.MissingJsonPath";
    public const string MissingModel = "Run.MissingModel";
    public const string MissingName = "Run.MissingName";
    public const string MissingPath = "Run.MissingPath";
    public const string MissingProgram = "Run.MissingProgram";
    public const string MissingQuestion = "Run.MissingQuestion";
    public const string MissingScript = "Run.MissingScript";
    public const string MissingScriptExtension = "Run.MissingScriptExtension";
    public const string MissingStep = "Run.MissingStep";
    public const string MissingTargetWindow = "Run.MissingTargetWindow";
    public const string MissingVariable = "Run.MissingVariable";
    public const string MissingWindow = "Run.MissingWindow";
    public const string NoAutoHotkey = "Run.NoAutoHotkey";
    public const string NoDevice = "Run.NoDevice";
    public const string NoDriver = "Run.NoDriver";
    public const string NoDriverAttached = "Run.NoDriverAttached";
    public const string NoDriverKey = "Run.NoDriverKey";
    public const string NoDriverLibrary = "Run.NoDriverLibrary";
    public const string NoDriverServer = "Run.NoDriverServer";
    public const string NoDriverTyping = "Run.NoDriverTyping";
    public const string NoEntryPoint = "Run.NoEntryPoint";
    public const string NoForegroundWindow = "Run.NoForegroundWindow";
    public const string NoFreeName = "Run.NoFreeName";
    public const string NoGamepad = "Run.NoGamepad";
    public const string NoGraphicsCapture = "Run.NoGraphicsCapture";
    public const string NoKeys = "Run.NoKeys";
    public const string NoPackages = "Run.NoPackages";
    public const string NoPython = "Run.NoPython";
    public const string NoSuchColumn = "Run.NoSuchColumn";
    public const string NoSuchProcess = "Run.NoSuchProcess";
    public const string NoSuchRun = "Run.NoSuchRun";
    public const string NoSuchStep = "Run.NoSuchStep";
    public const string NotAList = "Run.NotAList";
    public const string NotALookingStep = "Run.NotALookingStep";
    public const string NotANumber = "Run.NotANumber";
    public const string NothingToRethrow = "Run.NothingToRethrow";
    public const string NuGetFailed = "Run.NuGetFailed";
    public const string OneColumnOnly = "Run.OneColumnOnly";
    public const string OutOfRange = "Run.OutOfRange";
    public const string PipFailed = "Run.PipFailed";
    public const string PixelOutsideScreen = "Run.PixelOutsideScreen";
    public const string PowerRefused = "Run.PowerRefused";
    public const string ProcessTimeout = "Run.ProcessTimeout";
    public const string ProgramFailed = "Run.ProgramFailed";
    public const string ProgramNotFound = "Run.ProgramNotFound";
    public const string RecordWhereNumber = "Run.RecordWhereNumber";
    public const string RegexTimeout = "Run.RegexTimeout";
    public const string ScreenNotStable = "Run.ScreenNotStable";
    public const string ScriptBridgeLost = "Run.ScriptBridgeLost";
    public const string ScriptCompileFailed = "Run.ScriptCompileFailed";
    public const string ScriptFailed = "Run.ScriptFailed";
    public const string SoundRefused = "Run.SoundRefused";
    public const string StepFailedByMacro = "Run.StepFailedByMacro";
    public const string TextNotFound = "Run.TextNotFound";
    public const string Timeout = "Run.Timeout";
    public const string TooManyRuns = "Run.TooManyRuns";
    public const string UndeclaredResult = "Run.UndeclaredResult";
    public const string UnknownBrightnessAction = "Run.UnknownBrightnessAction";
    public const string UnknownGamepadButton = "Run.UnknownGamepadButton";
    public const string UnknownGamepadStick = "Run.UnknownGamepadStick";
    public const string UnknownGamepadTrigger = "Run.UnknownGamepadTrigger";
    public const string UnknownInputMethodAction = "Run.UnknownInputMethodAction";
    public const string UnknownKey = "Run.UnknownKey";
    public const string UnknownLabel = "Run.UnknownLabel";
    public const string UnknownNotification = "Run.UnknownNotification";
    public const string UnknownPageKey = "Run.UnknownPageKey";
    public const string UnknownPowerAction = "Run.UnknownPowerAction";
    public const string UnknownScriptKind = "Run.UnknownScriptKind";
    public const string UnknownSound = "Run.UnknownSound";
    public const string UnknownSystemField = "Run.UnknownSystemField";
    public const string UnknownVolumeAction = "Run.UnknownVolumeAction";
    public const string Unsupported = "Run.Unsupported";
    public const string WaitColorTimeout = "Run.WaitColorTimeout";
    public const string WaitTimeout = "Run.WaitTimeout";
    public const string WindowFailed = "Run.WindowFailed";
    public const string WindowNotFound = "Run.WindowNotFound";
    public const string WindowRecordField = "Run.WindowRecordField";
    public const string WindowTimeout = "Run.WindowTimeout";

    /// <summary>Every failure key, in ordinal order.</summary>
    public static IReadOnlyList<string> All { get; } = Vocabulary.Of(typeof(RunKeys));
}

/// <summary>
/// Every key a run log line can carry. These word what happened; they are not values a macro
/// branches on, and a failure keeps its <see cref="RunKeys"/> key when it is logged.
/// </summary>
public static class LogKeys
{
    public const string Aborted = "Log.Aborted";
    public const string ActivatedWindow = "Log.ActivatedWindow";
    public const string AppendedFile = "Log.AppendedFile";
    public const string AskedNo = "Log.AskedNo";
    public const string AskedText = "Log.AskedText";
    public const string AskedYes = "Log.AskedYes";
    public const string Brightness = "Log.Brightness";
    public const string BrowserAddress = "Log.BrowserAddress";
    public const string BrowserAnswered = "Log.BrowserAnswered";
    public const string BrowserAtAddress = "Log.BrowserAtAddress";
    public const string BrowserChecked = "Log.BrowserChecked";
    public const string BrowserClicked = "Log.BrowserClicked";
    public const string BrowserDownloaded = "Log.BrowserDownloaded";
    public const string BrowserEvaluated = "Log.BrowserEvaluated";
    public const string BrowserFilled = "Log.BrowserFilled";
    public const string BrowserHovered = "Log.BrowserHovered";
    public const string BrowserLoaded = "Log.BrowserLoaded";
    public const string BrowserPressed = "Log.BrowserPressed";
    public const string BrowserRead = "Log.BrowserRead";
    public const string BrowserReadAll = "Log.BrowserReadAll";
    public const string BrowserReadPage = "Log.BrowserReadPage";
    public const string BrowserReadTable = "Log.BrowserReadTable";
    public const string BrowserScrolled = "Log.BrowserScrolled";
    public const string BrowserSelected = "Log.BrowserSelected";
    public const string BrowserTab = "Log.BrowserTab";
    public const string BrowserUploaded = "Log.BrowserUploaded";
    public const string BrowserWaited = "Log.BrowserWaited";
    public const string CalledMacro = "Log.CalledMacro";
    public const string Capture = "Log.Capture";
    public const string Caught = "Log.Caught";
    public const string CancelledRun = "Log.CancelledRun";
    public const string CleanupFailed = "Log.CleanupFailed";
    public const string ClearedClipboard = "Log.ClearedClipboard";
    public const string ClickedElement = "Log.ClickedElement";
    public const string ClickedImage = "Log.ClickedImage";
    public const string ClickedObject = "Log.ClickedObject";
    public const string ClickedText = "Log.ClickedText";
    public const string ClipboardChanged = "Log.ClipboardChanged";
    public const string ClosedBrowser = "Log.ClosedBrowser";
    public const string ClosedTab = "Log.ClosedTab";
    public const string ClosedWindow = "Log.ClosedWindow";
    public const string CommandError = "Log.CommandError";
    public const string CommandFinished = "Log.CommandFinished";
    public const string CommandOutput = "Log.CommandOutput";
    public const string CommandStarted = "Log.CommandStarted";
    public const string CopiedFile = "Log.CopiedFile";
    public const string CopiedFilesToClipboard = "Log.CopiedFilesToClipboard";
    public const string CopiedFromWindow = "Log.CopiedFromWindow";
    public const string CopiedImageToClipboard = "Log.CopiedImageToClipboard";
    public const string CopiedToClipboard = "Log.CopiedToClipboard";
    public const string DeletedFile = "Log.DeletedFile";
    public const string Detected = "Log.Detected";
    public const string DetectedNothing = "Log.DetectedNothing";
    public const string ElementChecked = "Log.ElementChecked";
    public const string ElementExpanded = "Log.ElementExpanded";
    public const string ElementFilled = "Log.ElementFilled";
    public const string ElementFound = "Log.ElementFound";
    public const string ElementMissing = "Log.ElementMissing";
    public const string ElementWhere = "Log.ElementWhere";
    public const string FailureLog = "Log.FailureLog";
    public const string FailureLogFailed = "Log.FailureLogFailed";
    public const string FileLeftAlone = "Log.FileLeftAlone";
    public const string Finally = "Log.Finally";
    public const string Finished = "Log.Finished";
    public const string FocusedWindow = "Log.FocusedWindow";
    public const string FrameStill = "Log.FrameStill";
    public const string GamepadLetGoFailed = "Log.GamepadLetGoFailed";
    public const string GamepadReleased = "Log.GamepadReleased";
    public const string ImageFound = "Log.ImageFound";
    public const string ImageMissing = "Log.ImageMissing";
    public const string InvokedElement = "Log.InvokedElement";
    public const string KeysSent = "Log.KeysSent";
    public const string LetGo = "Log.LetGo";
    public const string LetGoFailed = "Log.LetGoFailed";
    public const string ListedDisplays = "Log.ListedDisplays";
    public const string ListedFiles = "Log.ListedFiles";
    public const string ListFieldMissing = "Log.ListFieldMissing";
    public const string ListFieldNotNumber = "Log.ListFieldNotNumber";
    public const string ListRowNotRecord = "Log.ListRowNotRecord";
    public const string ListedPrograms = "Log.ListedPrograms";
    public const string ListedWindows = "Log.ListedWindows";
    public const string LoadedVariables = "Log.LoadedVariables";
    public const string LogFileHeader = "Log.LogFileHeader";
    public const string LoopLimit = "Log.LoopLimit";
    public const string MadeFolder = "Log.MadeFolder";
    public const string MaximizedWindow = "Log.MaximizedWindow";
    public const string Message = "Log.Message";
    public const string MinimizedWindow = "Log.MinimizedWindow";
    public const string MovedFile = "Log.MovedFile";
    public const string MovedWindow = "Log.MovedWindow";
    public const string NoClipboardImage = "Log.NoClipboardImage";
    public const string Notified = "Log.Notified";
    public const string OpenedBrowser = "Log.OpenedBrowser";
    public const string PackageError = "Log.PackageError";
    public const string PackageInstalled = "Log.PackageInstalled";
    public const string PackageOutput = "Log.PackageOutput";
    public const string PackageReady = "Log.PackageReady";
    public const string Pasted = "Log.Pasted";
    public const string PastedText = "Log.PastedText";
    public const string PlayedSound = "Log.PlayedSound";
    public const string Power = "Log.Power";
    public const string ProcessExited = "Log.ProcessExited";
    public const string ProgramDetails = "Log.ProgramDetails";
    public const string ProgramFound = "Log.ProgramFound";
    public const string RanScript = "Log.RanScript";
    public const string ReadClipboard = "Log.ReadClipboard";
    public const string ReadClipboardFiles = "Log.ReadClipboardFiles";
    public const string ReadClipboardImage = "Log.ReadClipboardImage";
    public const string ReadElement = "Log.ReadElement";
    public const string ReadFile = "Log.ReadFile";
    public const string ReadFileLines = "Log.ReadFileLines";
    public const string ReadTable = "Log.ReadTable";
    public const string RecycledFile = "Log.RecycledFile";
    public const string RemovedFolder = "Log.RemovedFolder";
    public const string RestoredWindow = "Log.RestoredWindow";
    public const string Retry = "Log.Retry";
    public const string SavedPicture = "Log.SavedPicture";
    public const string SavedVariables = "Log.SavedVariables";
    public const string SawColor = "Log.SawColor";
    public const string ScriptError = "Log.ScriptError";
    public const string ScriptNotTidied = "Log.ScriptNotTidied";
    public const string ScriptOutput = "Log.ScriptOutput";
    public const string ScriptSaid = "Log.ScriptSaid";
    public const string ScriptStep = "Log.ScriptStep";
    public const string ScrolledElement = "Log.ScrolledElement";
    public const string SelectedItem = "Log.SelectedItem";
    public const string Set = "Log.Set";
    public const string Skipped = "Log.Skipped";
    public const string SoundOff = "Log.SoundOff";
    public const string SoundOn = "Log.SoundOn";
    public const string Speed = "Log.Speed";
    public const string Start = "Log.Start";
    public const string StartedProgram = "Log.StartedProgram";
    public const string StartedRun = "Log.StartedRun";
    public const string Stopped = "Log.Stopped";
    public const string StoppedProgram = "Log.StoppedProgram";
    public const string StopRequested = "Log.StopRequested";
    public const string TextFound = "Log.TextFound";
    public const string TextMissing = "Log.TextMissing";
    public const string UnknownCondition = "Log.UnknownCondition";
    public const string Unzipped = "Log.Unzipped";
    public const string Volume = "Log.Volume";
    public const string WaitedFor = "Log.WaitedFor";
    public const string WaitedRun = "Log.WaitedRun";
    public const string WindowAppeared = "Log.WindowAppeared";
    public const string WindowBox = "Log.WindowBox";
    public const string WroteFile = "Log.WroteFile";
    public const string Zipped = "Log.Zipped";

    /// <summary>Every log key, in ordinal order.</summary>
    public static IReadOnlyList<string> All { get; } = Vocabulary.Of(typeof(LogKeys));
}

/// <summary>How a whole run ended. These are not failure keys.</summary>
public static class StatusKeys
{
    public const string Finished = "Status.Finished";

    public const string Stopped = "Status.Stopped";

    public const string Aborted = "Status.Aborted";

    /// <summary>Every status key, in ordinal order.</summary>
    public static IReadOnlyList<string> All { get; } = [Aborted, Finished, Stopped];
}

/// <summary>Reads the keys declared as constants, so no second handwritten list can drift away.</summary>
internal static class Vocabulary
{
    public static IReadOnlyList<string> Of(System.Type type)
        => [.. type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)];
}
