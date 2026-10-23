using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

/// <summary>The space in which an input device's x and y are counted.</summary>
public enum InputCoordinateSpace
{
    /// <summary>Screen pixels, the desktop's own space.</summary>
    Screen,

    /// <summary>Pixels from the target window's client-area top-left corner.</summary>
    WindowClient,
}

/// <summary>The keyboard and the mouse.</summary>
public interface IInputDevice
{
    /// <summary>
    /// What the x and y of this device mean. The front and driver devices take screen pixels;
    /// a background device takes client-area pixels of the window it posts at.
    /// </summary>
    InputCoordinateSpace CoordinateSpace => InputCoordinateSpace.Screen;

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
    /// <summary>
    /// How the desk is laid out right now: one rectangle covering every display, which is what "the
    /// whole screen" means, and the displays it is made of. Read afresh each time because a display
    /// is plugged in, unplugged or rearranged while the program runs.
    /// </summary>
    ScreenLayout Desktop { get; }

    /// <summary>The colour of one pixel.</summary>
    PixelColor PixelAt(int x, int y);

    /// <summary>
    /// Where the pointer is right now, in screen pixels. It is a fact about the screen rather than
    /// about the way input is sent, so it is asked here and nowhere else: the three ways of sending
    /// input and the windows whose pickers follow the pointer all mean the same position, and one
    /// place to read it is what keeps them from drifting apart.
    /// </summary>
    ScreenPoint Cursor { get; }

    /// <summary>Copies a region of the desktop, the way this machine reads it by itself.</summary>
    ImageFrame Capture(int x, int y, int width, int height);

    /// <summary>
    /// Copies a region of the screen, taking the pixels from the place and by the means the
    /// request names, and saying where the picture it hands back sits. A request that names a
    /// window is answered with what the window shows — which a covering window does not change —
    /// and one that does not is answered with the desktop.
    /// </summary>
    ScreenShot Capture(ScreenCaptureRequest request);

    /// <summary>
    /// Lets go of the capture sessions kept for reuse, without making the screen unusable: a later
    /// capture opens what it needs again. Switching the macros off asks for this because nothing is
    /// then waiting to look at the screen. A device with nothing to keep answers by doing nothing.
    /// </summary>
    void ReleaseCaptures()
    {
    }

    /// <summary>
    /// How long one refresh of the screen lasts, in milliseconds. It is the shortest wait between
    /// two looks at the screen that can see something the look before it did not, because a picture
    /// that has not been drawn again cannot differ from the one already read; a step that waits for
    /// something to turn up looks again on this beat unless it was told another. A device that
    /// cannot say answers with 16, one refresh of a 60 Hz display.
    /// </summary>
    int RefreshMilliseconds => 16;
}

/// <summary>Looking for a picture on screen.</summary>
public interface IVisionDevice
{
    /// <summary>Reads a reference image from disk, or null when the file cannot be read.</summary>
    ImageFrame? Load(string path);

    /// <summary>
    /// Every place <paramref name="needle"/> appears in <paramref name="haystack"/>, surest first
    /// and at most <see cref="VisionQuery.Limit"/> of them, looked for the way the query asks. Two
    /// hits closer together than the reference picture count as one, because they are the same
    /// thing seen twice. Looking by features answers with one place at most: what those pairs say
    /// is where the whole of it landed, not where every one of them is.
    /// </summary>
    IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle, VisionQuery query);
}

/// <summary>Reading text off the screen.</summary>
public interface IOcrDevice
{
    /// <summary>Every piece of text in the frame, in reading order.</summary>
    IReadOnlyList<TextSpan> Recognize(ImageFrame frame);
}

/// <summary>
/// Looking for things in a picture with a detection model the user brought along: what a step hands
/// over is a region, and what comes back is where each thing is and which kind of thing it is.
/// </summary>
/// <remarks>
/// The model is named by the path of the file it lives at rather than held by the device, because
/// which model a step uses is the step's own business: a macro that watches for one kind of thing
/// and another that watches for something else may each bring their own. Loading a model costs a
/// moment, so a device that has already loaded one keeps it until the program ends.
/// </remarks>
public interface IDetectorDevice
{
    /// <summary>
    /// Everything the model finds in <paramref name="frame"/>, surest first and at most
    /// <see cref="DetectionQuery.Limit"/> of them, with the coordinates counted from the corner of
    /// the picture that was handed over. A model the device cannot read or run is the caller's
    /// answer to give rather than a stack trace: it comes back saying which of the two happened.
    /// </summary>
    IReadOnlyList<DetectionHit> Detect(string modelPath, ImageFrame frame, DetectionQuery query);
}

/// <summary>Finding and driving windows through UI Automation.</summary>
public interface IUiDevice
{
    bool Exists(UiQuery query, int timeoutMs);

    /// <summary>
    /// The element at one place on the screen, or null when nothing Automation can name is there.
    /// This is the question the element picker asks while the pointer moves: it is about a place
    /// rather than about a rule, which is the other way round from every other call here.
    /// </summary>
    UiElementInfo? ElementAt(int x, int y);

    /// <summary>Clicks an element with one of the mouse buttons.</summary>
    bool Click(UiQuery query, string button);

    /// <summary>
    /// Works the element's default action without a mouse: the button is pressed the way the
    /// element itself knows how, so the pointer stays where it was and the window it sits in is
    /// not brought forward. False when the element offers no way to be worked that way — that is
    /// an answer about the element rather than a failure of its own.
    /// </summary>
    bool Invoke(UiQuery query);

    /// <summary>
    /// Everything one element says about itself right now, or null when there is no such element.
    /// A step that has to decide what to do with a control — is it usable, is it showing, what is
    /// in it — asks here rather than guessing from what it looked like a moment ago.
    /// </summary>
    UiElementFacts? Facts(UiQuery query);

    /// <summary>
    /// Brings the window a query names to the front. The window is recognised by the same rule every
    /// other step recognises one with, so the query carries both halves of it.
    /// </summary>
    bool FocusWindow(UiQuery query);

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
    /// Whether the file's last line is still open: something is written there and it does not end
    /// in a line break, so whatever comes next has to put one there first to get a line of its own.
    /// A file that is missing, or empty, has no open line — its first line must not be pushed down
    /// by a blank one.
    /// </summary>
    bool HasOpenLine(string path, string encoding);

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
    /// Opens a file for reading a piece at a time, which is what a step that only walks over the
    /// bytes needs: working out the checksum of a file that does not fit in memory is the reason
    /// this is here rather than reading the whole thing as <see cref="ReadBytes"/> does.
    /// </summary>
    Stream OpenRead(string path);

    /// <summary>
    /// Removes a file. A file sent to the recycle bin is still there to be put back, which is what
    /// a delete driven by a written-down macro usually wants.
    /// </summary>
    void Delete(string path, bool toRecycleBin);

    /// <summary>
    /// Copies a file onto the target, replacing whatever is there. Whether replacing it is what
    /// the macro wanted is the step's own question, and it has been asked by the time this is
    /// called — the answer decides whether this is called at all.
    /// </summary>
    void Copy(string from, string to);

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
    void Move(string from, string to);

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
/// The main window handle and title are zero and empty while the program has no main window yet,
/// which is ordinary for a console program and for a window that is still starting up.
/// </summary>
public sealed record ProcessDetails(
    int Id,
    string Name,
    string Path,
    double MemoryMb,
    double CpuSeconds,
    long MainWindowHandle = 0,
    string MainWindowTitle = "");

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

    /// <summary>
    /// Starts a browser and opens a page at this address, under the profile named when one is. A
    /// profile is where the browser keeps what a site remembers about the person — the sign-in above
    /// all — so a macro that logs in once goes on finding itself logged in; no profile is a browser
    /// that forgets, which is what a macro that should not touch somebody's own session wants.
    /// </summary>
    void Open(string browser, string url, bool headless, string profile);

    /// <summary>
    /// Sends the page to this address, waiting for it to load no longer than the step's own clock
    /// gives it.
    /// </summary>
    void GoTo(string url, int timeoutMs);

    /// <summary>Where the page is right now.</summary>
    string Url { get; }

    /// <summary>
    /// Clicks the element the target names, and answers with the selector that turned out to name
    /// it: a target written as several selectors is one question with several answers, and which
    /// one was used is what the run's own log has to report. A forced click is made even through
    /// whatever stands in the way of it, which is what a page that hides its own buttons behind an
    /// overlay needs.
    /// </summary>
    string Click(PageTarget target, ClickWay how, bool force, int timeoutMs);

    /// <summary>
    /// Types text into the element the target names, replacing what it held, and answers with the
    /// selector that named it. Typed one key at a time rather than written in at once, it is what a
    /// page that watches the typing itself — a search box that offers suggestions, a form that
    /// checks each field as it is left — needs; the gap between two keys is the step's own, and
    /// nought means the keys go as fast as they can, with no pause between them that a page could
    /// notice.
    /// </summary>
    string Fill(PageTarget target, string text, FillWay how, int intervalMs, int timeoutMs);

    /// <summary>
    /// One part of the element the target names, or of the whole page when there is no target. The
    /// whole-page reading is what a macro wants when it is after the content rather than one field,
    /// and it is what a step that named no selector at all is read as; it reads the writing the page
    /// shows, there being no box and no markup of its own to ask for the other two parts of.
    /// </summary>
    string Text(PageTarget? target, PagePart part, string attribute, int timeoutMs);

    /// <summary>
    /// Every element the target names, each read the way the step asked for, starting at the match
    /// the target named and stopping after <paramref name="maxItems"/> of them.
    /// </summary>
    /// <remarks>
    /// A page holds lists of the same thing — every row of a result, every link of a menu — and the
    /// second of them has no name of its own to write down: reading them one step at a time is not
    /// something the person writing the macro can express. What is read is every match of the one
    /// selector that turned out to name anything, in the order the page shows them.
    /// </remarks>
    IReadOnlyList<string> ReadAll(PageTarget target, PagePart part, string attribute, int maxItems,
        int timeoutMs);

    /// <summary>
    /// The table the target names, read out of the page's own markup: the names it writes above its
    /// columns, and its rows under them. A table that writes no such names comes back with none, and
    /// the engine calls the columns by letter; the most rows read is
    /// <paramref name="maxRows"/>.
    /// </summary>
    PageTable ReadTable(PageTarget target, int maxRows, int timeoutMs);

    /// <summary>Where the page is and what it calls itself.</summary>
    PageInfo ReadPage(int timeoutMs);

    /// <summary>
    /// Whether the element the target names is in the state asked for right now.
    /// </summary>
    /// <remarks>
    /// It is a look rather than a wait: a condition is asked again and again by whatever block holds
    /// it, and a step that waits for an element is a step of its own. A page holding fewer elements
    /// than the target asks for is answered the way it is everywhere else — as naming nothing —
    /// which is what "gone" and "off the page" mean.
    /// </remarks>
    bool Exists(PageTarget target, ElementWait state);

    /// <summary>
    /// Waits for the page to be at an address the step names, comparing it the way
    /// <paramref name="how"/> says, and gives up after <paramref name="timeoutMs"/> milliseconds
    /// having waited none of its own.
    /// </summary>
    /// <remarks>
    /// What a macro waits for after a click that goes somewhere is the address rather than an
    /// element, because the element it is after often lives on the page the click leads to and
    /// naming it too early finds the one on the page being left behind.
    /// </remarks>
    void WaitForUrl(string match, string how, int timeoutMs,
        TextCompareOptions options = default);

    /// <summary>Waits for the page to have got as far as the state names in its loading.</summary>
    void WaitForLoad(PageLoad state, int timeoutMs);

    /// <summary>
    /// Moves the page so that what the step is after is on screen: the element the target names, or
    /// the page itself by one screen, to the bottom or back to the top.
    /// </summary>
    /// <remarks>
    /// Scrolling is what a page that fetches more as it is read needs: the writing below the fold
    /// arrives because something scrolled to where it would have been, and there is no element to
    /// wait for until it has. A target is only needed by the one way that scrolls to an element.
    /// </remarks>
    void Scroll(PageTarget? target, ScrollWay how, int timeoutMs);

    /// <summary>
    /// Takes the next file the page hands over, saving it into <paramref name="folder"/>, and
    /// answers with where it went and what it is called.
    /// </summary>
    /// <remarks>
    /// With a target, the element it names is clicked and the download that click starts is the one
    /// taken; with none, the step waits for the next download to come from anywhere on the page,
    /// which is what a macro does when the page decides for itself that something is a file. A
    /// download that started before this step asked for it is handed over rather than thrown away.
    /// Nobody turning up within the step's own clock is the step running out of time.
    /// </remarks>
    DownloadResult Download(PageTarget? target, string folder, int timeoutMs);

    /// <summary>
    /// Answers the next dialog the page puts up: accepted or dismissed, with writing of the step's
    /// own when the dialog is the kind that asks for some.
    /// </summary>
    /// <remarks>
    /// It is armed before the steps that make the dialog appear rather than called after them,
    /// because a dialog nobody has answered yet stops the step that raised it: in every browser the
    /// click that opens one does not come back until somebody says yes or no, so a step that clicks
    /// and a step that answers afterwards cannot both run. Only the first dialog of a block is
    /// answered this way; the ones after it are dismissed, which is what a browser does with nobody
    /// listening at all.
    /// </remarks>
    void ArmDialog(bool accept, string text);

    /// <summary>
    /// What the dialog a block answered said, and lets go of the armed answer.
    /// <see langword="null"/> when no dialog turned up, which is a step that did not do what it says.
    /// </summary>
    string? TakeDialog();

    /// <summary>
    /// Runs a script inside the page and answers with what it returned, written as JSON.
    /// </summary>
    /// <remarks>
    /// This is the way round to whatever no action of the catalogue covers — reading a page's own
    /// state, working something out from what it shows — at the price of the macro depending on the
    /// page's own script. Written as JSON rather than as text, a record or a list the script hands
    /// back keeps its shape and is read with <c>field()</c> the way any other record is.
    /// </remarks>
    string Evaluate(string script, int timeoutMs);

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
    /// answering with the ways of naming what they clicked, the steadiest one first. An empty answer
    /// means they gave up, or nothing was clicked in time. The page is left the way it was either
    /// way.
    /// <paramref name="hint"/> is what the banner inside the page says, written in the reader's
    /// language by whoever asked for the pick — the engine holds no user-facing text of its own.
    /// </summary>
    PickResult Pick(string hint, int timeoutMs);

    /// <summary>
    /// Outlines the element the target names on the open page, so a selector can be checked against
    /// the page it was taken from without a run being started. Nothing found is an answer rather
    /// than a failure: the question was "did that take the right element?", and it is answered the
    /// same way the step itself would be, which of the target's selectors it used included.
    /// </summary>
    HighlightResult Highlight(PageTarget target);

    /// <summary>
    /// Picks an option of a dropdown the target names: the one whose value or visible text is
    /// <paramref name="value"/>, or the one at that position when <paramref name="byIndex"/> is set,
    /// counted from the top starting at 1. A dropdown the page itself draws out of other elements
    /// is not one of these, and is worked the way a person works it — click it, then click the
    /// option. Answers with the selector that named the dropdown.
    /// </summary>
    string Select(PageTarget target, string value, bool byIndex, int timeoutMs);

    /// <summary>
    /// Ticks or unticks a checkbox or a radio button, which is what a macro wants instead of
    /// clicking one: a click on an already-ticked box would untick it, while this says the state
    /// the macro means to leave behind. Asking for the state a box is already in does nothing
    /// rather than refusing. Answers with the selector that named the box.
    /// </summary>
    string Check(PageTarget target, bool wanted, int timeoutMs);

    /// <summary>
    /// Moves the pointer over the element the target names and leaves it there, which is what makes
    /// a menu, a tooltip or a row of buttons appear. Answers with the selector that named it.
    /// </summary>
    string Hover(PageTarget target, int timeoutMs);

    /// <summary>
    /// Presses one key while the page is the one listening, or while the element the target names
    /// is: the element takes the focus first, and no target at all leaves the focus where it is,
    /// which is how a macro sends a key to the page itself. The key is a name a macro writes, with
    /// keys joined by "+" for a chord. Answers with the selector that named the element, empty for
    /// a key sent to the page itself.
    /// </summary>
    string Press(PageTarget? target, string key, int timeoutMs);

    /// <summary>
    /// Waits for the element the target names to be in the state asked for, and gives up after
    /// <paramref name="timeoutMs"/> milliseconds, having waited none of its own. Running out of
    /// time is reported as such rather than as the browser failing, because what the step asked
    /// for is an answer to "did it turn up", and "no" is an answer. Answers with the selector that
    /// named the element, or nothing when the page never held one — which for "gone" and "off the
    /// page" is the state that was asked for.
    /// </summary>
    string? WaitFor(PageTarget target, ElementWait state, int timeoutMs);

    /// <summary>
    /// Hands files to a file input, which is the one thing on a page that cannot be done by typing:
    /// a browser refuses to be told a path by a script, so this is the way round it. Answers with
    /// the selector that named the input.
    /// </summary>
    string Upload(PageTarget target, IReadOnlyList<string> paths, int timeoutMs);

    /// <summary>Closes the browser and lets go of it, which a macro does when it is done.</summary>
    void Close();
}

/// <summary>What a step waits for an element of a page to be.</summary>
public enum ElementWait
{
    /// <summary>On the page and taking up room, so a person could see it.</summary>
    Visible,

    /// <summary>Gone, or on the page but taking up no room at all.</summary>
    Hidden,

    /// <summary>On the page, whether or not it can be seen.</summary>
    Attached,

    /// <summary>Not on the page at all, which is what a page that has thrown something away looks like.</summary>
    Detached,
}

/// <summary>Which part of an element a step reads, there being more than one thing to ask it.</summary>
/// <remarks>
/// What a box holds and what it shows are two different things: a password field shows dots and
/// holds the password, and a dropdown shows the option the person picked and holds the value behind
/// it. The markup is a third: an address a link goes to, a picture's description, a data attribute
/// the site keeps for itself.
/// </remarks>
public enum PagePart
{
    /// <summary>The writing the element shows.</summary>
    Text,

    /// <summary>What the element holds rather than shows.</summary>
    Value,

    /// <summary>One attribute of the element's markup, named by the step.</summary>
    Attribute,
}

/// <summary>How far along a page's loading a step waits for.</summary>
/// <remarks>
/// The three are the three moments a page can be said to have arrived at, and they are asked for by
/// name because which of them a macro wants is its own answer: a document that has been read is
/// enough for the writing on it, and the network falling quiet is what says everything the page
/// fetches by itself has come in.
/// </remarks>
public enum PageLoad
{
    /// <summary>The document has been read and drawn, and the page's own scripts have run.</summary>
    Ready,

    /// <summary>Everything the page itself asks for — its pictures, its stylesheets — has come in.</summary>
    Loaded,

    /// <summary>Nothing the page asks for in the background is still outstanding.</summary>
    Idle,
}

/// <summary>How a step clicks an element of a page.</summary>
public enum ClickWay
{
    /// <summary>One click with the left button, which is what almost every step means.</summary>
    Single,

    /// <summary>Two clicks in quick succession, which is how a page that opens something in place works.</summary>
    Double,

    /// <summary>One click with the right button, which is what opens a page's own menu.</summary>
    Right,
}

/// <summary>How a step puts writing into an element of a page.</summary>
public enum FillWay
{
    /// <summary>Written in at once, which is quicker and is what a plain field needs.</summary>
    Fill,

    /// <summary>Typed one key at a time, which is what a page watching the typing itself needs.</summary>
    Type,
}

/// <summary>Which way a step moves the page, when what it is after is not on screen yet.</summary>
public enum ScrollWay
{
    /// <summary>To the element the target names, which is also what makes a lazy page load it.</summary>
    Element,

    /// <summary>One screen further down, which is how a page that adds more as it is read is walked.</summary>
    Screen,

    /// <summary>All the way down, to whatever the page has at its end.</summary>
    Bottom,

    /// <summary>Back to the top, which is what a step that reads a page from its start needs.</summary>
    Top,
}

/// <summary>
/// A table of a page: the names written above its columns, and its rows.
/// </summary>
/// <remarks>
/// Both are read here rather than being worked out by the caller, because "is the first row a strip
/// of names or the first row of data" is a question about the markup that only the page can answer;
/// which name a column goes by when the page writes none is the engine's own business, since it is
/// the same answer the table reader of a window gives.
/// </remarks>
public sealed record PageTable(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>Where a page is and what it calls itself.</summary>
public sealed record PageInfo(string Url, string Title);

/// <summary>A file a page handed over: where it was saved, and what the page called it.</summary>
public sealed record DownloadResult(string Path, string Name);

/// <summary>
/// Which element of a page a step means, named the way a macro may name it.
/// </summary>
/// <remarks>
/// More than one selector is how a macro survives a page that has been redesigned: they are written
/// one to a line and tried in that order, and the first that finds anything is the one the step
/// works through. The selectors and <see cref="MatchIndex"/> travel together because the two answer
/// one question — which element — and a step that named no selector at all is a step that means the
/// page itself. <see cref="Conditions"/> belong to the same question: they are what tells one of
/// several elements that a selector names from the others, and they hold whichever of them the step
/// works through.
/// </remarks>
public sealed record PageTarget(
    IReadOnlyList<string> Selectors, int MatchIndex, IReadOnlyList<PageCondition> Conditions)
{
    /// <summary>
    /// The target a step's field holds: one selector to a line, blank lines dropped, and which of
    /// the elements a selector matches is the one meant, counted from 1, with the conditions the
    /// step adds when a selector on its own names more than the one it wants.
    /// </summary>
    public static PageTarget Of(string text, int matchIndex,
        IReadOnlyList<PageCondition>? conditions = null) => new(
            [
                .. text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0),
            ],
            matchIndex,
            conditions ?? []);
}

/// <summary>
/// One more thing the right element has to be, beyond the way it is named, which is what tells it
/// from the others when a selector names several that look alike.
/// </summary>
public sealed record PageCondition(PageFilter How, string Value);

/// <summary>
/// How a step narrows down the elements a selector names, told the way the browser engine itself
/// offers to do it rather than worked out here: every one of these is a question the engine behind
/// the page already answers, so a step is narrowed the same way a person programming the browser
/// would narrow it.
/// </summary>
/// <remarks>
/// All of them hold at once, which is why the words read as conditions rather than as choices. The
/// first of them is not a condition on the element but on where it sits, and the rest are what
/// Playwright calls the element's filters: text it writes, elements it holds, and whether it can be
/// seen. A page shows a thing once and keeps a second copy of it hidden, so "only the visible one"
/// is the condition a person reaches for most.
/// </remarks>
public enum PageFilter
{
    /// <summary>Look for the element inside the container this names, rather than anywhere on the page.</summary>
    Inside,

    /// <summary>The element holds an element this names.</summary>
    Has,

    /// <summary>The element holds nothing this names.</summary>
    HasNot,

    /// <summary>What the element writes holds this text, whichever case and however the spacing runs.</summary>
    HasText,

    /// <summary>What the element writes holds no such text.</summary>
    HasNotText,

    /// <summary>Only elements a person could see, which is what tells a shown thing from its hidden twin.</summary>
    Visible,
}

/// <summary>
/// Which kind of naming a picked selector is, so the interface can say it in the reader's own words
/// and nothing outside the device has to know how a page is asked about its elements.
/// </summary>
public enum PickKind
{
    /// <summary>A data attribute a site keeps for its own tests, which is meant to survive a redesign.</summary>
    DataAttribute,

    /// <summary>A role together with the name read off it, which is how a screen reader finds it.</summary>
    Role,

    /// <summary>
    /// The text of the <c>&lt;label&gt;</c> a form control belongs to, which is the way a person
    /// reads the field rather than the way the form submits it.
    /// </summary>
    Label,

    /// <summary>The text the element shows.</summary>
    Text,

    /// <summary>The grey hint a box shows before anything is typed into it.</summary>
    Placeholder,

    /// <summary>The words a picture is described by, for a reader who cannot see it.</summary>
    AltText,

    /// <summary>The words a tooltip shows, or the name a control offers when nothing else names it.</summary>
    Title,

    /// <summary>The element's id.</summary>
    Id,

    /// <summary>The name a form field is submitted under.</summary>
    NameAttribute,

    /// <summary>The label a screen reader reads out for the element.</summary>
    AriaLabel,

    /// <summary>A path of tags from the top of the page, which is what is left when nothing above fits.</summary>
    Path,
}

/// <summary>One way of naming the element a person picked, and how many elements of the page it names.</summary>
/// <remarks>
/// How many matters as much as which: a selector naming three elements is still usable, because the
/// step says which of them it means, while one that gets there by a path of tags is the first to
/// break when the page is redesigned. Someone writing a macro wants both of those in front of them
/// before choosing what to leave the step holding.
/// </remarks>
public sealed record PickCandidate(PickKind Kind, string Selector, int Matches);

/// <summary>
/// What a pick answered: the selector to write down — the steadiest of them — and every way of
/// naming the same element that the page offered. An empty selector means the person gave up or
/// clicked nothing in time, and then there is nothing to write down either.
/// </summary>
public sealed record PickResult(string Selector, IReadOnlyList<PickCandidate> Candidates);

/// <summary>
/// What outlining a target found: whether anything matched at all, which of the target's selectors
/// was the one that did, and how many elements it names.
/// </summary>
public sealed record HighlightResult(bool Found, int Line, int Matches);

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
    /// The window Windows currently calls the foreground window, or null when nothing has one.
    /// The default is the first visible window a device lists, which is what a stand-in desktop
    /// has to offer; the Windows device asks the machine directly.
    /// </summary>
    WindowInfo? Foreground() => List().FirstOrDefault();

    /// <summary>
    /// Every window that matches <paramref name="value"/> the way <paramref name="match"/> and
    /// <paramref name="compare"/> say, ignoring case, in the order <see cref="List"/> gives them,
    /// or nothing when none does. An empty value matches the frontmost window alone — the one window
    /// a step that says nothing means — unless <paramref name="everyWhenEmpty"/> is set, which is
    /// what a step that lists windows means by saying nothing: every window there is. The interface
    /// asks for all of them so that what a step would recognise can be shown rather than guessed at,
    /// and the rule itself stays in one place.
    /// </summary>
    IReadOnlyList<WindowInfo> Filter(string value, WindowMatch match,
        string compare = TextCompare.Contains, bool everyWhenEmpty = false,
        TextCompareOptions options = default);

    /// <summary>
    /// The first window that matches <paramref name="value"/> the way <paramref name="match"/>
    /// and <paramref name="compare"/> say, ignoring case, or null when nothing does. An empty value
    /// matches the frontmost window, whichever of the three parts it is asked to look at. A
    /// <paramref name="compare"/> of <see cref="TextCompare.Regex"/> with a pattern the machine
    /// cannot read throws <see cref="ArgumentException"/>, which the run turns into a failed step.
    /// </summary>
    WindowInfo? Find(string value, WindowMatch match,
        string compare = TextCompare.Contains, TextCompareOptions options = default);

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

    /// <summary>
    /// How big a window's client area is, in pixels: the part inside the border and under the title
    /// bar, which is the part the window draws in and the only part a search inside it can look at.
    /// </summary>
    ScreenSize ClientSize(long handle);
}

/// <summary>Everything a macro can do to the machine, in one place.</summary>
public interface IDeviceLayer
{
    /// <summary>
    /// The keyboard and the mouse as this machine reports them, which is what the triggers, the
    /// pickers and the recorder listen to. It is here rather than inside the devices because the
    /// machine allows one global hook per process: whatever needs the events subscribes to this
    /// one while it works, and a layer handed in by a check can carry a hook that is not the real
    /// one.
    /// </summary>
    IInputHook Hook { get; }

    /// <summary>
    /// A recorder for this machine's input, one per recording. Recording is not something a macro
    /// does while it runs, so it is made on demand rather than held: a recorder that outlived its
    /// recording would go on taking subscriptions for a keyboard nobody is writing down.
    /// </summary>
    IInputRecorder CreateRecorder();

    /// <summary>
    /// The word that something about the windows changed, which is how a window that appears and
    /// goes again between two looks is still noticed. A machine with nothing to say answers with
    /// one whose <see cref="IWindowEvents.Available"/> is false, and the caller goes on looking.
    /// </summary>
    IWindowEvents WindowEvents { get; }

    /// <summary>
    /// The controller in the person's hands, read rather than listened to. It is here beside the
    /// hook for the same reason the hook is: a trigger bound to a controller button and a recording
    /// of one both read it, and a layer handed in by a check carries a controller the check can
    /// play rather than the one on the machine.
    /// </summary>
    IGamepadReading GamepadReading { get; }

    /// <summary>
    /// Driver-level input, which rests on two pieces of other projects: a kernel driver that is
    /// installed once, and a server that has to be running. Holding it here is what lets the window
    /// that explains it be driven without either one being on the machine.
    /// </summary>
    IDriverInput Driver { get; }

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

    /// <summary>
    /// How this machine reads a picture of the screen. It is beside the devices rather than inside
    /// the screen device because it is a choice about this machine that every device layer has an
    /// answer to, rather than a way of reading pixels that a device may or may not offer.
    /// </summary>
    ICaptureChoice Capture { get; }

    IScreenDevice Screen { get; }

    IVisionDevice Vision { get; }

    IOcrDevice Ocr { get; }

    /// <summary>
    /// Looking for things in a picture with a model the macro brought along. A layer with no model
    /// behind it refuses every step that asks for one.
    /// </summary>
    IDetectorDevice Detector { get; }

    IUiDevice Ui { get; }

    IFileDevice Files { get; }

    IClipboardDevice Clipboard { get; }

    IProcessDevice Processes { get; }

    /// <summary>
    /// Scripts of the languages this program runs itself, and the packages they ask for. A script
    /// runs in a process of its own with a way back into the macro, so it is a device like the
    /// browser is rather than something the engine does by hand.
    /// </summary>
    IScriptDevice Scripts { get; }

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

    /// <summary>
    /// A hook that refuses every subscription, the way the rest of this layer refuses every call:
    /// a machine with no devices has nothing to say about the keyboard either.
    /// </summary>
    public IInputHook Hook => _refusal;

    public IInputRecorder CreateRecorder() => _refusal;

    /// <summary>
    /// A machine that says nothing about its windows, which is an answer rather than a refusal:
    /// the caller keeps looking on its own, exactly as it would on a Windows too old to say.
    /// </summary>
    public IWindowEvents WindowEvents { get; } = new NoWindowEvents();

    /// <summary>
    /// No controller to read, which is an answer rather than a refusal: a trigger bound to a
    /// controller button is simply never answered, the way it is on a machine with nothing plugged in.
    /// </summary>
    public IGamepadReading GamepadReading { get; } = new NoGamepadReading();

    public IDriverInput Driver => _refusal;

    public IInputDevice Input => _refusal;

    /// <summary>This layer has no second way to send input, so every route is refused alike.</summary>
    public IInputRouter Inputs { get; } = new SingleInputRouter(new Refusal());

    /// <summary>Nothing here can be a controller.</summary>
    public IGamepadDevice Gamepad => _refusal;

    /// <summary>
    /// The automatic choice, which is what the engine takes to mean "read it the way this machine
    /// would read it anyway": a layer with no devices has no other answer to give.
    /// </summary>
    public ICaptureChoice Capture { get; } = CaptureChoice.Current;

    public IScreenDevice Screen => _refusal;

    public IVisionDevice Vision => _refusal;

    public IOcrDevice Ocr => _refusal;

    public IDetectorDevice Detector => _refusal;

    public IUiDevice Ui => _refusal;

    public IFileDevice Files => _refusal;

    public IClipboardDevice Clipboard => _refusal;

    public IProcessDevice Processes => _refusal;

    public IScriptDevice Scripts => _refusal;

    public ISystemDevice System => _refusal;

    public IWindowDevice Windows => _refusal;

    public IBrowserDevice Browser => _refusal;

    /// <summary>Answers every request with "not available", naming what was asked for.</summary>
    private sealed class Refusal
        : IInputDevice, IGamepadDevice, IScreenDevice, IVisionDevice, IOcrDevice, IDetectorDevice,
          IUiDevice, IFileDevice, IClipboardDevice, IProcessDevice, IScriptDevice, ISystemDevice,
          IWindowDevice, IBrowserDevice, IInputHook, IInputRecorder, IDriverInput
    {
        // Named through the interface because the input device already has a KeyDown and a KeyUp
        // that send a key rather than report one.
        event Action<KeyHookEvent>? IInputHook.KeyDown
        {
            add => throw Missing("the keyboard");
            remove => throw Missing("the keyboard");
        }

        event Action<KeyHookEvent>? IInputHook.KeyUp
        {
            add => throw Missing("the keyboard");
            remove => throw Missing("the keyboard");
        }

        event Action<MouseHookEvent>? IInputHook.MouseDown
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<MouseHookEvent>? IInputHook.MouseUp
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<MouseMovedHookEvent>? IInputHook.Moved
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<MouseMovedRelativeHookEvent>? IInputHook.MovedRelative
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        event Action<WheelHookEvent>? IInputHook.Wheel
        {
            add => throw Missing("the mouse");
            remove => throw Missing("the mouse");
        }

        public bool IsRunning => false;

        public bool IgnoreInjected { get; set; }

        /// <summary>
        /// A machine with no recorder records no controller either; the switch is answered rather than
        /// refused because getting a recorder at all is what is refused, before this is ever asked.
        /// </summary>
        public bool RecordController { get; set; } = true;

        event Action<RecordedInput>? IInputRecorder.Captured
        {
            add => throw Missing("a recorder");
            remove => throw Missing("a recorder");
        }

        event Action? IInputRecorder.StopRequested
        {
            add => throw Missing("a recorder");
            remove => throw Missing("a recorder");
        }

        public void Start() => throw Missing("a recorder");

        public void Stop() => throw Missing("a recorder");

        public void Dispose() => throw Missing("a recorder");

        public DriverInputState State => throw Missing("driver-level input");

        public bool AttachAtStart
        {
            get => throw Missing("driver-level input");
            set => throw Missing("driver-level input");
        }

        public System.Threading.Tasks.Task WarmUp() => throw Missing("driver-level input");

        public void HandOver() => throw Missing("driver-level input");

        public void Connect() => throw Missing("a controller");

        public void Button(string button, bool down) => throw Missing("a controller");

        public void Stick(string stick, int x, int y) => throw Missing("a controller");

        public void Trigger(string trigger, int amount) => throw Missing("a controller");

        public void ReleaseAll() => throw Missing("a controller");

        public bool Ready(string browser) => throw Missing("a browser");

        public string InstallHint => throw Missing("a browser");

        public bool IsOpen => throw Missing("a browser");

        public void Open(string browser, string url, bool headless, string profile)
            => throw Missing("a browser");

        public void GoTo(string url, int timeoutMs) => throw Missing("a browser");

        public string Url => throw Missing("a browser");

        public string Click(PageTarget target, ClickWay how, bool force, int timeoutMs)
            => throw Missing("a browser");

        public string Fill(PageTarget target, string text, FillWay how, int intervalMs, int timeoutMs)
            => throw Missing("a browser");

        public string Text(PageTarget? target, PagePart part, string attribute, int timeoutMs)
            => throw Missing("a browser");

        public IReadOnlyList<string> ReadAll(PageTarget target, PagePart part, string attribute,
            int maxItems, int timeoutMs) => throw Missing("a browser");

        public PageTable ReadTable(PageTarget target, int maxRows, int timeoutMs)
            => throw Missing("a browser");

        public PageInfo ReadPage(int timeoutMs) => throw Missing("a browser");

        public bool Exists(PageTarget target, ElementWait state) => throw Missing("a browser");

        public void WaitForUrl(string match, string how, int timeoutMs,
            TextCompareOptions options) => throw Missing("a browser");

        public void WaitForLoad(PageLoad state, int timeoutMs) => throw Missing("a browser");

        public void Scroll(PageTarget? target, ScrollWay how, int timeoutMs)
            => throw Missing("a browser");

        public DownloadResult Download(PageTarget? target, string folder, int timeoutMs)
            => throw Missing("a browser");

        public void ArmDialog(bool accept, string text) => throw Missing("a browser");

        public string? TakeDialog() => throw Missing("a browser");

        public string Evaluate(string script, int timeoutMs) => throw Missing("a browser");

        public void SwitchTab(TabChoice choice, int index, string match) => throw Missing("a browser");

        public void CloseTab() => throw Missing("a browser");

        public PickResult Pick(string hint, int timeoutMs) => throw Missing("a browser");

        public HighlightResult Highlight(PageTarget target) => throw Missing("a browser");

        public string Select(PageTarget target, string value, bool byIndex, int timeoutMs)
            => throw Missing("a browser");

        public string Check(PageTarget target, bool wanted, int timeoutMs) => throw Missing("a browser");

        public string Hover(PageTarget target, int timeoutMs) => throw Missing("a browser");

        public string Press(PageTarget? target, string key, int timeoutMs) => throw Missing("a browser");

        public string? WaitFor(PageTarget target, ElementWait state, int timeoutMs)
            => throw Missing("a browser");

        public string Upload(PageTarget target, IReadOnlyList<string> paths, int timeoutMs)
            => throw Missing("a browser");

        public void Close() => throw Missing("a browser");

        public string BaseFolder => throw Missing("files");

        public string Resolve(string path) => throw Missing("files");

        public bool Exists(string path) => throw Missing("files");

        public string ReadText(string path, string encoding) => throw Missing("files");

        public void WriteText(string path, string text, bool append, string encoding)
            => throw Missing("files");

        public bool HasOpenLine(string path, string encoding) => throw Missing("files");

        public byte[] ReadBytes(string path) => throw Missing("files");

        public void WriteBytes(string path, byte[] bytes) => throw Missing("files");

        public Stream OpenRead(string path) => throw Missing("files");

        public void Delete(string path, bool toRecycleBin) => throw Missing("files");

        public void Copy(string from, string to) => throw Missing("files");

        public void Move(string from, string to) => throw Missing("files");

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

        public ScriptResult RunPython(PythonScript script, IScriptHost host, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("Python scripts");

        public ScriptResult InstallPip(PipRequest request, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("Python packages");

        public ScriptResult RunCSharp(CSharpScript script, IScriptHost host, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("C# scripts");

        public ScriptResult RunAutoHotkey(AutoHotkeyScript script, IScriptHost host,
            Action<string> onOutput, Action<string> onError,
            System.Threading.CancellationToken token)
            => throw Missing("AutoHotkey scripts");

        public ScriptResult InstallNuGet(NuGetRequest request, Action<string> onOutput,
            Action<string> onError, System.Threading.CancellationToken token)
            => throw Missing("NuGet packages");

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

        IReadOnlyList<WindowInfo> IWindowDevice.Filter(string value, WindowMatch match,
            string compare, bool everyWhenEmpty, TextCompareOptions options)
            => throw Missing("windows");

        WindowInfo? IWindowDevice.Find(string value, WindowMatch match, string compare,
            TextCompareOptions options)
            => throw Missing("windows");

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

        ScreenSize IWindowDevice.ClientSize(long handle) => throw Missing("windows");

        public ScreenPoint Cursor => throw Missing("the pointer position");

        public ScreenLayout Desktop => throw Missing("the screen");

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

        public ScreenShot Capture(ScreenCaptureRequest request) => throw Missing("the screen");

        public ImageFrame? Load(string path) => throw Missing("image matching");

        public IReadOnlyList<ImageMatch> FindAll(ImageFrame haystack, ImageFrame needle,
            VisionQuery query) => throw Missing("image matching");

        public IReadOnlyList<TextSpan> Recognize(ImageFrame frame) => throw Missing("text recognition");

        public IReadOnlyList<DetectionHit> Detect(string modelPath, ImageFrame frame,
            DetectionQuery query) => throw Missing("object detection");

        public bool Exists(UiQuery query, int timeoutMs) => throw Missing("UI Automation");

        public UiElementInfo? ElementAt(int x, int y) => throw Missing("UI Automation");

        public bool Click(UiQuery query, string button) => throw Missing("UI Automation");

        public bool Invoke(UiQuery query) => throw Missing("UI Automation");

        public UiElementFacts? Facts(UiQuery query) => throw Missing("UI Automation");

        public bool FocusWindow(UiQuery query) => throw Missing("UI Automation");

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
