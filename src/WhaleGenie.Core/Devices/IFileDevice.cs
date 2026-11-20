using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Devices;

// Files and folders, and the entries a listing returns.
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
    /// what was inside it rather than a second copy of the folder's own name;
    /// <paramref name="includeTopFolder"/> asks for that second copy on purpose.
    /// </summary>
    void Zip(string folder, string to, bool includeTopFolder);

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
    IReadOnlyList<FileEntry> List(string folder, IReadOnlyList<string> patterns, bool recurse, int depth,
        ListKind kind = ListKind.Files);
}

/// <summary>
/// 列文件夹时收哪一类东西。默认只收文件，跟这个参数出现之前一样；收文件夹是为了"每个子文件夹
/// 各做一遍"这种宏——按名字找得到一个路径，却拿不到它下面有哪几个文件夹。
/// </summary>
public enum ListKind
{
    /// <summary>Files only, which is what every caller meant before folders could be asked for.</summary>
    Files,

    /// <summary>Folders only: their names, under the folder that was named.</summary>
    Folders,

    /// <summary>Both, in one list, ordered by whatever the step asks to order by.</summary>
    Both,
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
