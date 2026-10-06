using System;
using System.IO;

using Viktor.Models;

namespace Viktor.Execution;

/// <summary>
/// One macro's watch on a file or a folder: which change counts, and whether a change is waiting
/// to be acted on.
/// </summary>
/// <remarks>
/// One save is usually several events in a row — an editor writes a temporary file and renames it
/// over the original, and a plain write can report both a new file and a change to it. The events
/// are collected here and only handed over once they have stopped coming, so one save is one run
/// of the macro rather than three.
/// </remarks>
internal sealed class FileWatch : IDisposable
{
    /// <summary>How long the events of a single change are allowed to keep arriving.</summary>
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How long to wait before watching a path again that could not be watched, such as one whose
    /// folder has not been made yet.
    /// </summary>
    private static readonly TimeSpan RetryGap = TimeSpan.FromSeconds(5);

    private readonly object _gate = new();
    private readonly string _path;
    private readonly FileChangeKind _kind;
    private readonly string _filter;
    private readonly bool _subfolders;

    private FileSystemWatcher? _watcher;
    private DateTime _retryAt;
    private DateTime _lastEvent;
    private bool _pending;

    private FileWatch(string path, string signature, FileChangeKind kind, string filter, bool subfolders)
    {
        _path = path;
        _kind = kind;
        _filter = filter;
        _subfolders = subfolders;
        Signature = signature;
    }

    /// <summary>The settings this watch was built for, so a caller can tell when it is out of date.</summary>
    public string Signature { get; }

    /// <summary>
    /// The settings a watch would be built for. A macro re-pointed at another file, or asking
    /// about something else, is given a watch of its own; there is nothing worth keeping from the
    /// old one beyond its name.
    /// </summary>
    public static string SignatureOf(MacroItem macro, string path)
        => string.Join('\u001f', path, macro.WatchPath, macro.WatchChange, macro.WatchFilter,
            macro.WatchSubfolders);

    public static FileWatch Create(MacroItem macro, string path)
        => new(path, SignatureOf(macro, path), macro.WatchChange, macro.WatchFilter,
            macro.WatchSubfolders);

    /// <summary>
    /// Makes sure the file system is being listened to. A path that cannot be watched yet — the
    /// folder it belongs to does not exist — is left and tried again later, so a macro can wait
    /// for something to appear.
    /// </summary>
    public void Ensure(DateTime now)
    {
        lock (_gate)
        {
            if (_watcher is not null || now < _retryAt)
            {
                return;
            }

            if (!Build())
            {
                _retryAt = now + RetryGap;
            }
        }
    }

    /// <summary>
    /// Whether a change has been seen and the flurry of events around it has died down. The
    /// answer is given out once: the caller is expected to act on it, and a change that arrives
    /// while it is being acted on is a new one.
    /// </summary>
    public bool Take(DateTime now)
    {
        lock (_gate)
        {
            if (!_pending || now - _lastEvent < QuietPeriod)
            {
                return false;
            }

            _pending = false;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_watcher is { } watcher)
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
                _watcher = null;
            }
        }
    }

    /// <summary>
    /// Builds the watcher. A watch has to sit on a folder, so a file — including one that is not
    /// there yet — is watched by watching the folder it belongs to and asking for that one name.
    /// </summary>
    private bool Build()
    {
        var folder = Directory.Exists(_path) ? _path : Path.GetDirectoryName(_path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return false;
        }

        var watchingFolder = Directory.Exists(_path);
        var filter = watchingFolder ? _filter : Path.GetFileName(_path);
        if (string.IsNullOrEmpty(filter))
        {
            filter = "*";
        }

        var watcher = new FileSystemWatcher(folder, filter)
        {
            IncludeSubdirectories = watchingFolder && _subfolders,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
        };

        watcher.Created += OnChanged;
        watcher.Changed += OnChanged;
        watcher.Deleted += OnChanged;
        watcher.Renamed += OnRenamed;
        watcher.Error += OnError;
        watcher.EnableRaisingEvents = true;

        _watcher = watcher;
        return true;
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Note(e.ChangeType);

    /// <summary>A rename is the old name going away and the new one appearing in one go.</summary>
    private void OnRenamed(object sender, RenamedEventArgs e)
        => Note(WatcherChangeTypes.Created | WatcherChangeTypes.Deleted);

    /// <summary>
    /// The file system dropped events because more came in than the buffer could hold. What was
    /// missed is unknown, so this counts as a change: running the macro once too often beats
    /// leaving it waiting for something that has already happened.
    /// </summary>
    private void OnError(object sender, ErrorEventArgs e) => Note(WatcherChangeTypes.All);

    private void Note(WatcherChangeTypes change)
    {
        if (!Matches(change))
        {
            return;
        }

        lock (_gate)
        {
            _pending = true;
            _lastEvent = DateTime.Now;
        }
    }

    /// <summary>Whether a change of this kind is one the macro asked about.</summary>
    private bool Matches(WatcherChangeTypes change) => _kind switch
    {
        FileChangeKind.Any => true,
        FileChangeKind.Created => change.HasFlag(WatcherChangeTypes.Created),
        FileChangeKind.Changed => change.HasFlag(WatcherChangeTypes.Changed),
        _ => change.HasFlag(WatcherChangeTypes.Deleted),
    };
}
