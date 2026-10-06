using System;
using System.Collections.Generic;

namespace Viktor.Core.Devices;

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
    IReadOnlyList<IReadOnlyList<string>> ReadTable(UiQuery query, int limit);
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

    /// <summary>Removes a file.</summary>
    void Delete(string path);

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
    /// Moves a file to another place, renaming it when the new name is in the same folder. A
    /// move across drives is a copy and a delete underneath, which is why it can take a while.
    /// </summary>
    void Move(string from, string to, bool overwrite);

    /// <summary>The files in a folder, as full paths.</summary>
    IReadOnlyList<string> List(string folder, string pattern, bool recurse);
}

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
    CommandResult Run(string fileName, string arguments, string workingDirectory, int timeoutMs);
}

/// <summary>Facts about this machine that are not a file or a device.</summary>
public interface ISystemDevice
{
    /// <summary>One named fact about the machine, such as <c>userName</c> or <c>tempFolder</c>.</summary>
    string Info(string field);

    /// <summary>An environment variable, or an empty string when it is not set.</summary>
    string Environment(string name);
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

    IScreenDevice Screen { get; }

    IVisionDevice Vision { get; }

    IOcrDevice Ocr { get; }

    IUiDevice Ui { get; }

    IFileDevice Files { get; }

    IClipboardDevice Clipboard { get; }

    IProcessDevice Processes { get; }

    ISystemDevice System { get; }

    IWindowDevice Windows { get; }
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

    public IScreenDevice Screen => _refusal;

    public IVisionDevice Vision => _refusal;

    public IOcrDevice Ocr => _refusal;

    public IUiDevice Ui => _refusal;

    public IFileDevice Files => _refusal;

    public IClipboardDevice Clipboard => _refusal;

    public IProcessDevice Processes => _refusal;

    public ISystemDevice System => _refusal;

    public IWindowDevice Windows => _refusal;

    /// <summary>Answers every request with "not available", naming what was asked for.</summary>
    private sealed class Refusal
        : IInputDevice, IScreenDevice, IVisionDevice, IOcrDevice, IUiDevice, IFileDevice,
          IClipboardDevice, IProcessDevice, ISystemDevice, IWindowDevice
    {
        public string BaseFolder => throw Missing("files");

        public string Resolve(string path) => throw Missing("files");

        public bool Exists(string path) => throw Missing("files");

        public string ReadText(string path, string encoding) => throw Missing("files");

        public void WriteText(string path, string text, bool append, string encoding)
            => throw Missing("files");

        public void Delete(string path) => throw Missing("files");

        public void Copy(string from, string to, bool overwrite) => throw Missing("files");

        public void Move(string from, string to, bool overwrite) => throw Missing("files");

        public void CreateFolder(string path) => throw Missing("files");

        public void DeleteFolder(string path, bool recurse) => throw Missing("files");

        public void Unzip(string from, string folder, bool overwrite) => throw Missing("files");

        public IReadOnlyList<string> List(string folder, string pattern, bool recurse) => throw Missing("files");

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

        public CommandResult Run(string fileName, string arguments, string workingDirectory, int timeoutMs)
            => throw Missing("command lines");

        public string Info(string field) => throw Missing("system information");

        public string Environment(string name) => throw Missing("environment variables");

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

        public IReadOnlyList<IReadOnlyList<string>> ReadTable(UiQuery query, int limit)
            => throw Missing("UI Automation");

        private static DeviceUnavailableException Missing(string capability) => new(capability);
    }
}
