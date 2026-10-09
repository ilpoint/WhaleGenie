using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Devices;

/// <summary>The keyboard and the mouse.</summary>
public interface IInputDevice
{
    /// <summary>Where the pointer is right now.</summary>
    ScreenPoint Cursor { get; }

    void KeyPress(string key, int holdMs);

    void KeyDown(string key);

    void KeyUp(string key);

    /// <summary>Presses every key in the chord, holds them together, then releases them.</summary>
    void Hotkey(IReadOnlyList<string> keys, int holdMs);

    void TypeText(string text, int intervalMs);

    void MoveMouse(int x, int y, int durationMs);

    /// <summary>
    /// Walks the pointer along a planned path, finishing on its last point. The first point is
    /// where the pointer already is, so only the stops after it are sent. A move that goes
    /// straight is the same call with a line as the path.
    /// </summary>
    void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs);

    void MoveMouseRelative(int dx, int dy, int durationMs);

    void MouseDown(string button, int x, int y);

    void MouseUp(string button, int x, int y);

    void Click(string button, int x, int y, int clicks, int intervalMs);

    /// <summary>
    /// Scrolls the wheel; the direction is <c>up</c>, <c>down</c>, <c>left</c> or <c>right</c>,
    /// and <paramref name="delta"/> is measured in wheel units, where 120 units make one notch.
    /// A whole number of notches is what a wheel normally moves in; the finer units are there so
    /// a step can ask for a part of one.
    /// </summary>
    void Scroll(string direction, int delta, int x, int y);

    void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps);

    /// <summary>
    /// Presses the button at the first point, walks the rest of the path with it held, and
    /// releases on the last one.
    /// </summary>
    void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs);
}

/// <summary>The screen: its size, its pixels, and pictures of it.</summary>
public interface IScreenDevice
{
    /// <summary>Size of the primary screen, in pixels.</summary>
    ScreenSize PrimarySize { get; }

    /// <summary>The colour of one pixel.</summary>
    PixelColor PixelAt(int x, int y);

    /// <summary>Copies a region of the desktop.</summary>
    ImageFrame Capture(int x, int y, int width, int height);
}

/// <summary>Looking for a picture on screen.</summary>
public interface IVisionDevice
{
    /// <summary>Reads a reference image from disk, or null when the file cannot be read.</summary>
    ImageFrame? Load(string path);

    /// <summary>The best place <paramref name="needle"/> appears in <paramref name="haystack"/>.</summary>
    ImageMatch? Find(ImageFrame haystack, ImageFrame needle, double confidencePercent);

    /// <summary>
    /// Every place <paramref name="needle"/> appears in <paramref name="haystack"/>, best first and
    /// at most <paramref name="limit"/> of them. Two hits closer together than the reference picture
    /// count as one, because they are the same thing seen twice.
    /// </summary>
    IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
        double confidencePercent, int limit);
}

/// <summary>Reading text off the screen.</summary>
public interface IOcrDevice
{
    /// <summary>Every piece of text in the frame, in reading order.</summary>
    IReadOnlyList<TextSpan> Recognize(ImageFrame frame, string language);
}

/// <summary>Finding and driving windows through UI Automation.</summary>
public interface IUiDevice
{
    bool Exists(UiQuery query, int timeoutMs);

    /// <summary>Clicks an element with one of the mouse buttons.</summary>
    bool Click(UiQuery query, string button);

    bool FocusWindow(string title);

    string? GetText(UiQuery query);

    /// <summary>Writes into an element, optionally clearing what was there first.</summary>
    bool SetText(UiQuery query, string text, bool clearFirst);

    /// <summary>
    /// Every element the query describes, in the order a person counts them on screen and at most
    /// <paramref name="limit"/> of them. Each one carries the rectangle it occupies, which is what
    /// a step that reads where something is, or that measures from it, needs.
    /// </summary>
    IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit);

    /// <summary>
    /// Picks one entry of a list, a drop-down, a set of tabs or a menu: the one showing
    /// <paramref name="text"/>, or the one at <paramref name="itemIndex"/> counted from one when
    /// that is above zero. A drop-down that has not been opened yet is opened on the way.
    /// </summary>
    bool Select(UiQuery query, string text, int itemIndex);

    /// <summary>
    /// Turns a check box, a switch or a radio button on or off, or flips it when
    /// <paramref name="state"/> is null.
    /// </summary>
    bool SetChecked(UiQuery query, bool? state);

    /// <summary>Expands or collapses a node, or flips it: <c>expand</c>, <c>collapse</c>, <c>toggle</c>.</summary>
    bool SetExpanded(UiQuery query, string action);

    /// <summary>Scrolls an element inside its own container until it can be seen.</summary>
    bool ScrollIntoView(UiQuery query);

    /// <summary>
    /// Reads a table or a grid into rows of cell text, at most <paramref name="limit"/> rows.
    /// A list whose rows are rows rather than a real table reads the same way, one entry per row.
    /// </summary>
    UiTable ReadTable(UiQuery query, int limit);
}

/// <summary>Files on disk.</summary>
public interface IFileDevice
{
    /// <summary>Where a relative path is read from and written to.</summary>
    string BaseFolder { get; }

    /// <summary>
    /// The full path a macro's path stands for: a relative one is read from
    /// <see cref="BaseFolder"/>, which is where every other file action reads and writes too.
    /// </summary>
    string Resolve(string path);

    /// <summary>Whether a file or a folder is there.</summary>
    bool Exists(string path);

    /// <summary>Reads a text file whole, in the encoding the caller names.</summary>
    string ReadText(string path, string encoding);

    /// <summary>Writes a text file, making the folders on the way when they are missing.</summary>
    void WriteText(string path, string text, bool append, string encoding);

    /// <summary>
    /// Reads a file whole, as the bytes it is made of. Anything that is not text in any encoding
    /// comes in this way: a workbook is a zip before it is anything else.
    /// </summary>
    byte[] ReadBytes(string path);

    /// <summary>
    /// Writes a file of raw bytes, making the folders on the way when they are missing. Text
    /// actions cannot do this one: a picture is not text in any encoding.
    /// </summary>
    void WriteBytes(string path, byte[] bytes);

    /// <summary>
    /// Removes a file. A file sent to the recycle bin is still there to be put back, which is what
    /// a delete driven by a written-down macro usually wants.
    /// </summary>
    void Delete(string path, bool toRecycleBin);

    /// <summary>Copies a file.</summary>
    void Copy(string from, string to, bool overwrite);

    /// <summary>Makes a folder, along with any folders above it that are not there yet.</summary>
    void CreateFolder(string path);

    /// <summary>
    /// Removes a folder. A folder that still holds something is only removed when
    /// <paramref name="recurse"/> says so, so a macro cannot empty a tree by accident.
    /// </summary>
    void DeleteFolder(string path, bool recurse);

    /// <summary>
    /// Unpacks a zip file into a folder, making the folder when it is not there yet. Every entry
    /// keeps the place inside the zip it was stored in, so the shape of the archive comes out.
    /// </summary>
    void Unzip(string from, string folder, bool overwrite);

    /// <summary>
    /// Packs a folder into a zip file, making the folders above the zip when they are missing.
    /// The entries are stored relative to the folder itself, so unpacking this zip gives back
    /// what was inside it rather than a second copy of the folder's own name.
    /// </summary>
    void Zip(string folder, string to);

    /// <summary>
    /// Moves a file to another place, renaming it when the new name is in the same folder. A
    /// move across drives is a copy and a delete underneath, which is why it can take a while.
    /// </summary>
    void Move(string from, string to, bool overwrite);

    /// <summary>
    /// The files in a folder. Each one comes with what it takes to sort and weed them: a macro
    /// asking for "what changed today" turns that question into fields rather than a second look
    /// at the disk. <paramref name="patterns"/> are file-name wildcards; <paramref name="depth"/>
    /// is how many folders deep to go, where zero is the folder itself and below zero is no end
    /// to it.
    /// </summary>
    IReadOnlyList<FileEntry> List(string folder, IReadOnlyList<string> patterns, bool recurse, int depth);
}

/// <summary>One file a folder listing turned up, with what it says about itself.</summary>
public sealed record FileEntry(
    string Path,
    string Name,
    string Folder,
    long Size,
    DateTimeOffset Created,
    DateTimeOffset Modified,
    DateTimeOffset Accessed);

/// <summary>The clipboard: the text something last copied, and a way to put text there.</summary>
public interface IClipboardDevice
{
    /// <summary>
    /// A number that goes up whenever the clipboard changes. A macro waits for the next
    /// copy by remembering this and watching for it to move.
    /// </summary>
    int ChangeCount { get; }

    /// <summary>Whether there is text on the clipboard.</summary>
    bool HasText { get; }

    /// <summary>The text on the clipboard, or an empty string when there is none.</summary>
    string ReadText();

    /// <summary>
    /// The picture on the clipboard, or null when there is not one. A picture copied from the
    /// screen comes back the size it was copied at.
    /// </summary>
    ImageFrame? ReadImage();

    /// <summary>Replaces whatever is on the clipboard with this text.</summary>
    void WriteText(string text);

    /// <summary>Puts a picture on the clipboard, the same as copying one.</summary>
    void WriteImage(ImageFrame image);

    /// <summary>The paths of the files on the clipboard, or an empty list when there are none.</summary>
    IReadOnlyList<string> ReadFiles();

    /// <summary>
    /// Puts file paths on the clipboard the way copying files in Explorer does, so pasting drops
    /// the files themselves rather than their names.
    /// </summary>
    void WriteFiles(IReadOnlyList<string> paths);

    /// <summary>Empties the clipboard.</summary>
    void Clear();
}

/// <summary>Other programs: starting them, watching them, and stopping them.</summary>
public interface IProcessDevice
{
    /// <summary>Starts a program and hands back its process id.</summary>
    int Start(StartRequest request);

    /// <summary>The ids of the running processes with this name.</summary>
    IReadOnlyList<int> Find(string name);

    /// <summary>Every running process, by name, without repeats.</summary>
    IReadOnlyList<string> List();

    /// <summary>Whether the process with this id has finished.</summary>
    bool HasExited(int id);

    /// <summary>What a process returned, or null while it is still running.</summary>
    int? ExitCode(int id);

    /// <summary>Stops every process with this name; the answer is how many were stopped.</summary>
    int StopByName(string name, bool force);

    /// <summary>Stops one process by id.</summary>
    bool StopById(int id, bool force);

    /// <summary>Runs a program to the end and collects what it printed.</summary>
    CommandResult Run(CommandRequest request);

    /// <summary>
    /// What is known about one running program, picked by name or by id, or null when there is
    /// no such program. Whichever name matches first is the one described.
    /// </summary>
    ProcessDetails? Details(string target);
}

/// <summary>
/// One running program as the macro can see it. <paramref name="Path"/> is empty when Windows
/// will not say — a program running as another user or at a higher level keeps its file to itself.
/// </summary>
public sealed record ProcessDetails(
    int Id,
    string Name,
    string Path,
    double MemoryMb,
    double CpuSeconds);

/// <summary>Facts about this machine that are not a file or a device.</summary>
public interface ISystemDevice
{
    /// <summary>One named fact about the machine, such as <c>userName</c> or <c>tempFolder</c>.</summary>
    string Info(string field);

    /// <summary>An environment variable, or an empty string when it is not set.</summary>
    string Environment(string name);

    /// <summary>
    /// Asks the machine to do one of the things the Start menu's power button does.
    /// <paramref name="graceSeconds"/> is how long Windows warns before restarting or shutting
    /// down, and is ignored by the rest.
    /// </summary>
    void Power(PowerAction action, int graceSeconds);

    /// <summary>The volume of the speakers Windows is using, from 0 to 100.</summary>
    int Volume();

    /// <summary>Turns the speakers up or down to a level from 0 to 100.</summary>
    void SetVolume(int percent);

    /// <summary>Whether the sound is switched off.</summary>
    bool IsMuted();

    /// <summary>Switches the sound off, or back on.</summary>
    void SetMuted(bool muted);

    /// <summary>
    /// Plays one of the sounds this machine plays for an event, so a macro that has finished, or
    /// gone wrong, can say so while nobody is looking at the screen. A machine told to keep quiet
    /// plays nothing and is left alone; one with no sound device refuses, and the step says so.
    /// </summary>
    void PlaySound(SoundKind kind);

    /// <summary>
    /// Shows a notification beside the notification area, so a macro can say something happened
    /// while the window is out of the way. A machine with its notifications switched off shows
    /// nothing and is left alone; one where the program has nowhere to show it refuses, and the
    /// step says so.
    /// </summary>
    void Notify(string title, string text, NotificationKind kind);

    /// <summary>The keyboard layout the focused window is typing in, said the way a person would.</summary>
    string InputMethod();

    /// <summary>Every keyboard layout installed on this machine, said the same way.</summary>
    IReadOnlyList<string> InputMethods();

    /// <summary>
    /// Switches the focused window to the layout whose name matches and says which one it ended up
    /// on, or null when the machine has no such layout.
    /// </summary>
    string? SwitchInputMethod(string layout);

    /// <summary>The brightness of the screens, from 0 to 100.</summary>
    int Brightness();

    /// <summary>Turns the screens up or down to a brightness from 0 to 100.</summary>
    void SetBrightness(int percent);
}

/// <summary>
/// A browser the macro drives over its own automation protocol. Unlike the other devices this
/// one is stateful on purpose: a macro says "open this" and then "click that" the way a person
/// works, so the device keeps the page between steps until the macro closes it.
/// </summary>
/// <remarks>
/// A browser window holds several tabs, and the page a macro means is the one the person is
/// looking at rather than the first one that was opened. So the device keeps track of which tab it
/// is on, and the actions that move between tabs are what keeps that from being a guess.
/// </remarks>
public interface IBrowserDevice
{
    /// <summary>
    /// Whether this choice of browser can be started on this machine right now. The one Windows
    /// already ships — Edge — can always be used; the engines Playwright downloads by itself have
    /// to have been fetched first, so a machine that never ran a macro may not have them yet.
    /// </summary>
    bool Ready(string browser);

    /// <summary>How the browsers are put on the machine, told to the user when they are missing.</summary>
    string InstallHint { get; }

    /// <summary>
    /// Whether there is a page to drive right now. It is false again once the window the page was
    /// in has been closed, whether that was done by this program or by the person using it.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>Starts a browser and opens a page at this address.</summary>
    void Open(string browser, string url, bool headless);

    /// <summary>Sends the page to this address.</summary>
    void GoTo(string url);

    /// <summary>Where the page is right now.</summary>
    string Url { get; }

    /// <summary>Clicks the first element the selector names.</summary>
    void Click(string selector);

    /// <summary>Types text into the element the selector names, replacing what it held.</summary>
    void Fill(string selector, string text);

    /// <summary>
    /// The text the selector names, or the whole page's text when the selector is empty. The
    /// whole-page reading is what a macro wants when it is after the content rather than one field.
    /// </summary>
    string Text(string selector);

    /// <summary>
    /// Moves to another tab of the browser that is open, which is how a macro follows a click that
    /// opened one: the site hands the interesting page to a new tab, and the macro would otherwise
    /// go on aiming its steps at the tab it was already on — where the same site often has an
    /// element that looks just like the one it wants. A tab that cannot be found is refused rather
    /// than passed over, because every step after it would then act on the wrong page.
    /// </summary>
    void SwitchTab(TabChoice choice, int index, string match);

    /// <summary>
    /// Closes the tab the browser is on and moves to another one, which is what a macro does with
    /// the tab a click opened once it has read what it came for. Closing the last tab leaves no
    /// page behind, and the steps after it are refused like any other step with no browser open.
    /// </summary>
    void CloseTab();

    /// <summary>
    /// Puts the open page into picking mode and waits for the person to click an element on it,
    /// answering with a selector for what they clicked. An empty answer means they gave up, or
    /// nothing was clicked in time. The page is left the way it was either way.
    /// <paramref name="hint"/> is what the banner inside the page says, written in the reader's
    /// language by whoever asked for the pick — the engine holds no user-facing text of its own.
    /// </summary>
    string Pick(string hint, int timeoutMs);

    /// <summary>Closes the browser and lets go of it, which a macro does when it is done.</summary>
    void Close();
}

/// <summary>Which tab of the open browser a macro asked to move to.</summary>
public enum TabChoice
{
    /// <summary>
    /// The one that appeared last, which is where a click that opened a tab leaves the person.
    /// </summary>
    Newest,

    /// <summary>By number, counted from the left of the tab strip starting at 1.</summary>
    Index,

    /// <summary>The first tab whose title contains a piece of text.</summary>
    Title,

    /// <summary>The first tab whose address contains a piece of text.</summary>
    Address,
}

/// <summary>Open windows: finding them, moving them, and closing them.</summary>
public interface IWindowDevice
{
    /// <summary>Every top-level window that has a title, in the order Windows lists them.</summary>
    IReadOnlyList<WindowInfo> List();

    /// <summary>
    /// The first window that matches <paramref name="value"/> the way <paramref name="match"/>
    /// says, ignoring case, or null when nothing does. An empty value matches the frontmost
    /// window, whichever of the three it is asked to look at.
    /// </summary>
    WindowInfo? Find(string value, WindowMatch match);

    /// <summary>
    /// The name of the program that owns a window, without the ".exe", or an empty string when the
    /// window is gone. Looking a process up costs more than reading a title, which is why a step
    /// that only compares titles never causes one.
    /// </summary>
    string ProcessOf(long handle);

    /// <summary>The class a window was registered under, such as "Notepad", or an empty string.</summary>
    string ClassOf(long handle);

    /// <summary>Brings a window to the front, restoring it first if it was shrunk.</summary>
    bool Activate(long handle);

    bool Minimize(long handle);

    bool Maximize(long handle);

    /// <summary>Puts a shrunk or full-screen window back to its normal size.</summary>
    bool Restore(long handle);

    /// <summary>Asks a window to close, the same as clicking its close button.</summary>
    bool Close(long handle);

    /// <summary>Moves and resizes a window, in screen pixels.</summary>
    bool Move(long handle, int x, int y, int width, int height);

    /// <summary>
    /// Where a window's client area starts, in screen pixels: the point inside its border that
    /// the window itself counts from. A macro that means "100 pixels into the window" needs this
    /// rather than the window's outer corner, which the border and the title bar push around.
    /// </summary>
    ScreenPoint ClientOrigin(long handle);
}

/// <summary>Everything a macro can do to the machine, in one place.</summary>
public interface IDeviceLayer
{
    IInputDevice Input { get; }

    /// <summary>
    /// The input devices a step can choose between. A layer with only one way in answers with
    /// that one whatever the step asks for.
    /// </summary>
    IInputRouter Inputs { get; }

    /// <summary>
    /// The virtual controller a macro drives, which is a device of its own rather than another way
    /// of sending keyboard and mouse input. A layer with no controller refuses every step that
    /// asks for one.
    /// </summary>
    IGamepadDevice Gamepad { get; }

    IScreenDevice Screen { get; }

    IVisionDevice Vision { get; }

    IOcrDevice Ocr { get; }

    IUiDevice Ui { get; }

    IFileDevice Files { get; }

    IClipboardDevice Clipboard { get; }

    IProcessDevice Processes { get; }

    ISystemDevice System { get; }

    IWindowDevice Windows { get; }

    /// <summary>The browser a macro drives, when the actions for it are used.</summary>
    IBrowserDevice Browser { get; }
}

/// <summary>Thrown when an action needs something this machine cannot give it.</summary>
public sealed class DeviceUnavailableException(string capability)
    : Exception($"The device layer does not provide {capability}.")
{
    /// <summary>What was missing, for example <c>screen</c> or <c>ocr</c>.</summary>
    public string Capability { get; } = capability;
}

/// <summary>
/// Thrown when the device is there but the request makes no sense. The key is translated by
/// the interface, the way every other run failure is.
/// </summary>
public sealed class DeviceActionException(string key, string detail = "")
    : Exception(key)
{
    public string Key { get; } = key;

    public string Detail { get; } = detail;
}

/// <summary>
/// A device layer with nothing behind it. Every call is refused, which is what lets the
/// engine be tested, and lets a macro run on a machine with no devices attached.
/// </summary>
public sealed class NullDeviceLayer : IDeviceLayer
{
    /// <summary>The shared instance, which holds no state.</summary>
    public static NullDeviceLayer Instance { get; } = new();

    private readonly Refusal _refusal = new();

    public IInputDevice Input => _refusal;

    /// <summary>This layer has no second way to send input, so every route is refused alike.</summary>
    public IInputRouter Inputs { get; } = new SingleInputRouter(new Refusal());

    /// <summary>Nothing here can be a controller.</summary>
    public IGamepadDevice Gamepad => _refusal;

    public IScreenDevice Screen => _refusal;

    public IVisionDevice Vision => _refusal;

    public IOcrDevice Ocr => _refusal;

    public IUiDevice Ui => _refusal;

    public IFileDevice Files => _refusal;

    public IClipboardDevice Clipboard => _refusal;

    public IProcessDevice Processes => _refusal;

    public ISystemDevice System => _refusal;

    public IWindowDevice Windows => _refusal;

    public IBrowserDevice Browser => _refusal;

    /// <summary>Answers every request with "not available", naming what was asked for.</summary>
    private sealed class Refusal
        : IInputDevice, IGamepadDevice, IScreenDevice, IVisionDevice, IOcrDevice, IUiDevice,
          IFileDevice, IClipboardDevice, IProcessDevice, ISystemDevice, IWindowDevice,
          IBrowserDevice
    {
        public void Connect() => throw Missing("a controller");

        public void Button(string button, bool down) => throw Missing("a controller");

        public void Stick(string stick, int x, int y) => throw Missing("a controller");

        public void Trigger(string trigger, int amount) => throw Missing("a controller");

        public void ReleaseAll() => throw Missing("a controller");

        public bool Ready(string browser) => throw Missing("a browser");

        public string InstallHint => throw Missing("a browser");

        public bool IsOpen => throw Missing("a browser");

        public void Open(string browser, string url, bool headless) => throw Missing("a browser");

        public void GoTo(string url) => throw Missing("a browser");

        public string Url => throw Missing("a browser");

        public void Click(string selector) => throw Missing("a browser");

        public void Fill(string selector, string text) => throw Missing("a browser");

        public string Text(string selector) => throw Missing("a browser");

        public void SwitchTab(TabChoice choice, int index, string match) => throw Missing("a browser");

        public void CloseTab() => throw Missing("a browser");

        public string Pick(string hint, int timeoutMs) => throw Missing("a browser");

        public void Close() => throw Missing("a browser");

        public string BaseFolder => throw Missing("files");

        public string Resolve(string path) => throw Missing("files");

        public bool Exists(string path) => throw Missing("files");

        public string ReadText(string path, string encoding) => throw Missing("files");

        public void WriteText(string path, string text, bool append, string encoding)
            => throw Missing("files");

        public byte[] ReadBytes(string path) => throw Missing("files");

        public void WriteBytes(string path, byte[] bytes) => throw Missing("files");

        public void Delete(string path, bool toRecycleBin) => throw Missing("files");

        public void Copy(string from, string to, bool overwrite) => throw Missing("files");

        public void Move(string from, string to, bool overwrite) => throw Missing("files");

        public void CreateFolder(string path) => throw Missing("files");

        public void DeleteFolder(string path, bool recurse) => throw Missing("files");

        public void Unzip(string from, string folder, bool overwrite) => throw Missing("files");

        public void Zip(string folder, string to) => throw Missing("files");

        public IReadOnlyList<FileEntry> List(string folder, IReadOnlyList<string> patterns, bool recurse,
            int depth) => throw Missing("files");

        public int ChangeCount => throw Missing("the clipboard");

        public bool HasText => throw Missing("the clipboard");

        public ImageFrame? ReadImage() => throw Missing("the clipboard");

        public string ReadText() => throw Missing("the clipboard");

        public void WriteText(string text) => throw Missing("the clipboard");

        public void WriteImage(ImageFrame image) => throw Missing("the clipboard");

        public IReadOnlyList<string> ReadFiles() => throw Missing("the clipboard");

        public void WriteFiles(IReadOnlyList<string> paths) => throw Missing("the clipboard");

        public void Clear() => throw Missing("the clipboard");

        public int Start(StartRequest request) => throw Missing("other programs");

        public IReadOnlyList<int> Find(string name) => throw Missing("other programs");

        public IReadOnlyList<string> List() => throw Missing("other programs");

        public bool HasExited(int id) => throw Missing("other programs");

        public int? ExitCode(int id) => throw Missing("other programs");

        public int StopByName(string name, bool force) => throw Missing("other programs");

        public bool StopById(int id, bool force) => throw Missing("other programs");

        public CommandResult Run(CommandRequest request) => throw Missing("command lines");

        public ProcessDetails? Details(string target) => throw Missing("other programs");

        public string Info(string field) => throw Missing("system information");

        public string Environment(string name) => throw Missing("environment variables");

        public void Power(PowerAction action, int graceSeconds) => throw Missing("power");

        public int Volume() => throw Missing("the sound card");

        public void SetVolume(int percent) => throw Missing("the sound card");

        public bool IsMuted() => throw Missing("the sound card");

        public void SetMuted(bool muted) => throw Missing("the sound card");

        public void PlaySound(SoundKind kind) => throw Missing("the sound card");

        public void Notify(string title, string text, NotificationKind kind)
            => throw Missing("somewhere to show notifications");

        public string InputMethod() => throw Missing("a keyboard layout");

        public IReadOnlyList<string> InputMethods() => throw Missing("a keyboard layout");

        public string? SwitchInputMethod(string layout) => throw Missing("a keyboard layout");

        public int Brightness() => throw Missing("an adjustable screen");

        public void SetBrightness(int percent) => throw Missing("an adjustable screen");

        // Named through the interface because the process device already has a List and a Find
        // that take no such argument.
        IReadOnlyList<WindowInfo> IWindowDevice.List() => throw Missing("windows");

        WindowInfo? IWindowDevice.Find(string value, WindowMatch match) => throw Missing("windows");

        string IWindowDevice.ProcessOf(long handle) => throw Missing("windows");

        string IWindowDevice.ClassOf(long handle) => throw Missing("windows");

        bool IWindowDevice.Activate(long handle) => throw Missing("windows");

        bool IWindowDevice.Minimize(long handle) => throw Missing("windows");

        bool IWindowDevice.Maximize(long handle) => throw Missing("windows");

        bool IWindowDevice.Restore(long handle) => throw Missing("windows");

        bool IWindowDevice.Close(long handle) => throw Missing("windows");

        bool IWindowDevice.Move(long handle, int x, int y, int width, int height)
            => throw Missing("windows");

        ScreenPoint IWindowDevice.ClientOrigin(long handle) => throw Missing("windows");

        public ScreenPoint Cursor => throw Missing("the pointer position");

        public ScreenSize PrimarySize => throw Missing("the screen");

        public void KeyPress(string key, int holdMs) => throw Missing("the keyboard");

        public void KeyDown(string key) => throw Missing("the keyboard");

        public void KeyUp(string key) => throw Missing("the keyboard");

        public void Hotkey(IReadOnlyList<string> keys, int holdMs) => throw Missing("the keyboard");

        public void TypeText(string text, int intervalMs) => throw Missing("the keyboard");

        public void MoveMouse(int x, int y, int durationMs) => throw Missing("the mouse");

        public void MoveMouseAlong(IReadOnlyList<ScreenPoint> path, int durationMs)
            => throw Missing("the mouse");

        public void MoveMouseRelative(int dx, int dy, int durationMs) => throw Missing("the mouse");

        public void MouseDown(string button, int x, int y) => throw Missing("the mouse");

        public void MouseUp(string button, int x, int y) => throw Missing("the mouse");

        public void Click(string button, int x, int y, int clicks, int intervalMs) => throw Missing("the mouse");

        public void Scroll(string direction, int delta, int x, int y) => throw Missing("the mouse");

        public void Drag(string button, int startX, int startY, int endX, int endY, int durationMs, int steps)
            => throw Missing("the mouse");

        public void DragAlong(string button, IReadOnlyList<ScreenPoint> path, int durationMs)
            => throw Missing("the mouse");

        public PixelColor PixelAt(int x, int y) => throw Missing("the screen");

        public ImageFrame Capture(int x, int y, int width, int height) => throw Missing("the screen");

        public ImageFrame? Load(string path) => throw Missing("image matching");

        public ImageMatch? Find(ImageFrame haystack, ImageFrame needle, double confidencePercent)
            => throw Missing("image matching");

        public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
            double confidencePercent, int limit) => throw Missing("image matching");

        public IReadOnlyList<TextSpan> Recognize(ImageFrame frame, string language) => throw Missing("text recognition");

        public bool Exists(UiQuery query, int timeoutMs) => throw Missing("UI Automation");

        public bool Click(UiQuery query, string button) => throw Missing("UI Automation");

        public bool FocusWindow(string title) => throw Missing("UI Automation");

        public string? GetText(UiQuery query) => throw Missing("UI Automation");

        public bool SetText(UiQuery query, string text, bool clearFirst) => throw Missing("UI Automation");

        public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
            => throw Missing("UI Automation");

        public bool Select(UiQuery query, string text, int itemIndex) => throw Missing("UI Automation");

        public bool SetChecked(UiQuery query, bool? state) => throw Missing("UI Automation");

        public bool SetExpanded(UiQuery query, string action) => throw Missing("UI Automation");

        public bool ScrollIntoView(UiQuery query) => throw Missing("UI Automation");

        public UiTable ReadTable(UiQuery query, int limit) => throw Missing("UI Automation");

        private static DeviceUnavailableException Missing(string capability) => new(capability);
    }
}
