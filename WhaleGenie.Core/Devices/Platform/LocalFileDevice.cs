using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Enumeration;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Files on this machine. A path the macro writes on its own is taken to be inside the
/// macros folder, so a shared macro keeps its data together instead of scattering it
/// wherever WhaleGenie happened to be started from. A full path is used as written.
/// </summary>
public sealed class LocalFileDevice : IFileDevice
{
    /// <summary>
    /// Where a path a macro writes on its own is looked for when nothing else is said. The
    /// interface asks here too, so a path picked out of a dialog is stored the same way one typed
    /// by hand would be read.
    /// </summary>
    public static string DefaultBaseFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WhaleGenie");

    public LocalFileDevice(string? baseFolder = null)
    {
        BaseFolder = string.IsNullOrWhiteSpace(baseFolder)
            ? DefaultBaseFolder
            : baseFolder;
    }

    public string BaseFolder { get; }

    public string Resolve(string path) => Full(path);

    public bool Exists(string path)
    {
        var full = Full(path);
        return File.Exists(full) || Directory.Exists(full);
    }

    public string ReadText(string path, string encoding)
    {
        var full = Full(path);
        if (!File.Exists(full))
        {
            throw new DeviceActionException("Run.FileNotFound", path);
        }

        // The bytes are read whole rather than handed to a reader with an encoding on it, because
        // which encoding they are in is a question about the bytes. A file with no mark that cannot
        // be UTF-8 is this machine's own code page, and only looking at the bytes can say so.
        return Attempt(path, () => TextEncoding.Read(File.ReadAllBytes(full), encoding));
    }

    public void WriteText(string path, string text, bool append, string encoding)
    {
        var full = Full(path);
        var written = TextEncoding.Resolve(encoding);
        Attempt(path, () =>
        {
            var folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (append)
            {
                File.AppendAllText(full, text, written);
            }
            else
            {
                File.WriteAllText(full, text, written);
            }

            return true;
        });
    }

    public bool HasOpenLine(string path, string encoding)
    {
        var full = Full(path);
        if (!File.Exists(full))
        {
            return false;
        }

        return Attempt(path, () =>
        {
            using var file = File.OpenRead(full);
            if (file.Length == 0)
            {
                return false;
            }

            // The question is about the end of the file, so only the end is read: the file could be
            // a log with a year of lines in it. It is answered from the bytes rather than by decoding
            // them, because the tail can begin in the middle of a character, and a decoder handed
            // those bytes takes the break for the second half of the one before it — measured, on a
            // file ending in 世界 followed by a break. Every encoding the editor offers spells the
            // break itself in ASCII, in two bytes when the encoding is a wide one.
            var take = (int)Math.Min(8, file.Length);
            file.Seek(-take, SeekOrigin.End);
            var tail = new byte[take];
            file.ReadExactly(tail);

            if (TextEncoding.Wide(encoding))
            {
                return take < 2 || tail[^1] != 0x00 || tail[^2] is not (0x0A or 0x0D);
            }

            return tail[^1] is not (0x0A or 0x0D);
        });
    }

    public void WriteBytes(string path, byte[] bytes)
    {
        var full = Full(path);
        Attempt(path, () =>
        {
            var folder = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllBytes(full, bytes);
            return true;
        });
    }

    public byte[] ReadBytes(string path)
    {
        var full = Full(path);
        if (!File.Exists(full))
        {
            throw new DeviceActionException("Run.FileNotFound", path);
        }

        return Attempt(path, () => File.ReadAllBytes(full));
    }

    public void Delete(string path, bool toRecycleBin)
    {
        var full = Full(path);
        if (!File.Exists(full))
        {
            throw new DeviceActionException("Run.FileNotFound", path);
        }

        Attempt(path, () =>
        {
            if (toRecycleBin)
            {
                // The Windows shell does this, and the shell puts the file where the Recycle Bin
                // picks it up: a macro that deletes the wrong thing can be undone by a person,
                // which is worth more than the byte-for-byte speed of File.Delete.
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(full,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }
            else
            {
                File.Delete(full);
            }

            return true;
        });
    }

    public void Copy(string from, string to)
    {
        var source = Full(from);
        if (!File.Exists(source))
        {
            throw new DeviceActionException("Run.FileNotFound", from);
        }

        var target = Full(to);
        Attempt(from, () =>
        {
            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.Copy(source, target, overwrite: true);
            return true;
        });
    }

    public void Unzip(string from, string folder, bool overwrite)
    {
        var source = Full(from);
        if (!File.Exists(source))
        {
            throw new DeviceActionException("Run.FileNotFound", from);
        }

        var target = Full(folder);
        try
        {
            Attempt(from, () =>
            {
                Directory.CreateDirectory(target);
                ZipFile.ExtractToDirectory(source, target, overwrite);
                return true;
            });
        }
        catch (InvalidDataException)
        {
            // A file that is not an archive at all, or one that was cut off halfway through:
            // what the archive reader says about it is nothing a macro author can act on.
            throw new DeviceActionException("Run.BadZip", from);
        }
    }

    public void Zip(string folder, string to)
    {
        var source = Full(folder);
        if (!Directory.Exists(source))
        {
            throw new DeviceActionException("Run.FolderNotFound", folder);
        }

        var target = Full(to);
        Attempt(folder, () =>
        {
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            // The folder's own name is left out of the entries so that unpacking this zip hands
            // back what was inside the folder, the way its Unzip counterpart writes it out.
            ZipFile.CreateFromDirectory(source, target, CompressionLevel.Optimal, false);
            return true;
        });
    }

    public void CreateFolder(string path)
    {
        var full = Full(path);
        Attempt(path, () =>
        {
            Directory.CreateDirectory(full);
            return true;
        });
    }

    public void DeleteFolder(string path, bool recurse)
    {
        var full = Full(path);
        if (!Directory.Exists(full))
        {
            throw new DeviceActionException("Run.FolderNotFound", path);
        }

        Attempt(path, () =>
        {
            Directory.Delete(full, recurse);
            return true;
        });
    }

    public void Move(string from, string to)
    {
        var source = Full(from);
        if (!File.Exists(source))
        {
            throw new DeviceActionException("Run.FileNotFound", from);
        }

        var target = Full(to);
        Attempt(from, () =>
        {
            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.Move(source, target, overwrite: true);
            return true;
        });
    }

    public IReadOnlyList<FileEntry> List(string folder, IReadOnlyList<string> patterns, bool recurse,
        int depth)
    {
        var full = Full(folder);
        if (!Directory.Exists(full))
        {
            throw new DeviceActionException("Run.FolderNotFound", folder);
        }

        var wanted = patterns.Count == 0 ? ["*"] : patterns;
        var found = Attempt(folder, () =>
        {
            var entries = new List<FileEntry>();
            Walk(new DirectoryInfo(full), wanted, recurse ? depth : 0, entries);
            return (IReadOnlyList<FileEntry>)entries;
        });

        // The same file can answer to two filters (`*.txt` and `report*`), and a macro that loops
        // over the list must not do the same thing twice.
        return [.. found.DistinctBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Collects the files of one folder, then of the folders under it while <paramref name="left"/>
    /// still allows it: zero is this folder alone, and anything below zero is no end to it.
    /// </summary>
    private static void Walk(DirectoryInfo folder, IReadOnlyList<string> patterns, int left,
        List<FileEntry> into)
    {
        foreach (var file in folder.EnumerateFiles())
        {
            if (patterns.Any(pattern => FileSystemName.MatchesSimpleExpression(pattern, file.Name)))
            {
                into.Add(new FileEntry(file.FullName, file.Name, file.DirectoryName ?? "",
                    file.Length, file.CreationTime, file.LastWriteTime, file.LastAccessTime));
            }
        }

        if (left == 0)
        {
            return;
        }

        foreach (var child in folder.EnumerateDirectories())
        {
            Walk(child, patterns, left < 0 ? left : left - 1, into);
        }
    }

    /// <summary>Where a path points: inside the macros folder unless it is a full path.</summary>
    private string Full(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DeviceActionException("Run.MissingPath");
        }

        return Path.IsPathRooted(path) ? path : Path.Combine(BaseFolder, path);
    }

    /// <summary>Turns whatever the file system refused into something the step can report.</summary>
    private static T Attempt<T>(string path, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception error) when (error is IOException
                                          or UnauthorizedAccessException
                                          or NotSupportedException
                                          or ArgumentException
                                          or System.Security.SecurityException)
        {
            throw new DeviceActionException("Run.FileFailed", $"{path}: {error.Message}");
        }
    }
}
