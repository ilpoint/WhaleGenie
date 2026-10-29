using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend disk: files held in memory, keyed by full path, with the same questions a real disk
/// answers.
/// </summary>
/// <remarks>
/// A check that is about folders, encodings or the recycle bin needs a disk it can look inside, and
/// that is what <see cref="Store"/> and <see cref="Blobs"/> are for. It is a device of its own so a
/// check about something else — a click, say — never has to know what a file is.
/// </remarks>
internal sealed class FakeFiles(FakeDesk desk) : IFileDevice
{
    /// <summary>The pretend folder relative paths resolve under.</summary>
    public string BaseFolder { get; set; } = "G:\\fake";

    /// <summary>The full path a relative one stands for: the fake reads them from its base folder.</summary>
    public string Resolve(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(BaseFolder, path);

    /// <summary>The pretend files on disk, keyed by full path, for reading and copying.</summary>
    public Dictionary<string, string> Store { get; } = [];

    /// <summary>The pretend files of raw bytes on disk, keyed by the path they were written to.</summary>
    public Dictionary<string, byte[]> Blobs { get; } = [];

    /// <summary>The folders the fake has been told about, by name.</summary>
    public List<string> Folders { get; } = [];

    /// <summary>True when a folder should refuse to go away on its own, like a real non-empty one.</summary>
    public bool FolderHasFiles { get; set; }

    /// <summary>What listing a folder gives back.</summary>
    public List<string> FolderEntries { get; } = [];

    /// <summary>
    /// What each listed file says about itself. A file nothing is said about is empty and as old
    /// as the epoch, so a step that sorts or weeds by these is the only thing under test.
    /// </summary>
    public Dictionary<string, (long Size, DateTimeOffset Modified)> Facts { get; } = new();

    /// <summary>What <see cref="Exists"/> reports.</summary>
    public bool PathExists { get; set; }

    /// <summary>
    /// What reading a text file answers when that path was never written. A script step reads back
    /// the mark the command it started leaves behind, and a check that wants that command to look as
    /// though it came to an end of its own says so here.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Whether the fake's zip file unpacks, or turns out to be something else.</summary>
    public bool UnzipWorks { get; set; } = true;

    public bool Exists(string path)
    {
        desk.Note($"fileExists {path}");
        return PathExists || Store.ContainsKey(path) || Blobs.ContainsKey(path);
    }

    public string ReadText(string path, string encoding)
    {
        desk.Note($"readFile {path} {encoding}");
        return Store.TryGetValue(path, out var text) ? text : Text;
    }

    public byte[] ReadBytes(string path)
    {
        desk.Note($"readBytes {path}");
        return Blobs.TryGetValue(path, out var bytes) ? bytes : [];
    }

    public Stream OpenRead(string path)
    {
        desk.Note($"openRead {path}");
        return new MemoryStream(Blobs.TryGetValue(path, out var bytes) ? bytes : []);
    }

    public void WriteText(string path, string text, bool append, string encoding)
    {
        desk.Note($"writeFile {path} {text} {append} {encoding}");
        if (append && Store.TryGetValue(path, out var old))
        {
            Store[path] = old + text;
        }
        else
        {
            Store[path] = text;
        }
    }

    public bool HasOpenLine(string path, string encoding)
    {
        desk.Note($"openLine {path}");
        return Store.TryGetValue(path, out var text)
            && text.Length > 0
            && text[^1] is not ('\n' or '\r');
    }

    public void WriteBytes(string path, byte[] bytes)
    {
        desk.Note($"writeBytes {path} {bytes.Length}");
        Blobs[path] = bytes;
    }

    public void Delete(string path, bool toRecycleBin)
    {
        desk.Note($"deleteFile {path} {toRecycleBin}");
        Store.Remove(path);
    }

    public void Copy(string from, string to)
    {
        desk.Note($"copyFile {from} {to}");

        if (Store.TryGetValue(from, out var text))
        {
            Store[to] = text;
        }
    }

    public void Move(string from, string to)
    {
        desk.Note($"moveFile {from} {to}");

        if (Store.TryGetValue(from, out var text))
        {
            Store.Remove(from);
            Store[to] = text;
        }
    }

    public IReadOnlyList<FileEntry> List(string folder, IReadOnlyList<string> patterns,
        bool recurse, int depth)
    {
        desk.Note($"listFiles {folder} {string.Join(";", patterns)} {recurse} {depth}");
        return
        [
            .. FolderEntries.Select(path =>
            {
                var facts = Facts.TryGetValue(path, out var known)
                    ? known
                    : (Size: 0L, Modified: DateTimeOffset.UnixEpoch);
                return new FileEntry(path, Path.GetFileName(path),
                    Path.GetDirectoryName(path) ?? "", facts.Size, facts.Modified, facts.Modified,
                    facts.Modified);
            }),
        ];
    }

    public void CreateFolder(string path)
    {
        desk.Note($"createFolder {path}");
        Folders.Add(path);
    }

    public void Unzip(string from, string folder, bool overwrite)
    {
        desk.Note($"unzip {from} {folder} {overwrite}");
        if (UnzipWorks)
        {
            Store[Path.Combine(folder, "readme.txt")] = "unpacked";
        }
        else
        {
            throw new DeviceActionException("Run.FileFailed", $"{from}: not a zip file");
        }
    }

    public void Zip(string folder, string to)
    {
        desk.Note($"zip {folder} {to}");
        Store[to] = "zipped";
    }

    public void DeleteFolder(string path, bool recurse)
    {
        desk.Note($"deleteFolder {path} {recurse}");

        // A folder with something still in it is what the real one refuses to remove on its own,
        // and the fake has to answer the same way for the check to mean anything.
        if (FolderHasFiles && !recurse)
        {
            throw new DeviceActionException("Run.FileFailed", $"{path}: the folder is not empty");
        }

        Folders.Remove(path);
    }
}
