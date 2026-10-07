using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Files on this machine. A path the macro writes on its own is taken to be inside the
/// macros folder, so a shared macro keeps its data together instead of scattering it
/// wherever WhaleGenie happened to be started from. A full path is used as written.
/// </summary>
public sealed class LocalFileDevice : IFileDevice
{
    public LocalFileDevice(string? baseFolder = null)
    {
        BaseFolder = string.IsNullOrWhiteSpace(baseFolder)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WhaleGenie")
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

        // The encoding named is the one used to read, but a file that starts with a byte-order
        // mark still wins: a mark says what the file is, whatever the step guessed.
        var text = TextEncoding.Resolve(encoding);
        return Attempt(path, () => File.ReadAllText(full, text));
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

    public void Delete(string path)
    {
        var full = Full(path);
        if (!File.Exists(full))
        {
            throw new DeviceActionException("Run.FileNotFound", path);
        }

        Attempt(path, () =>
        {
            File.Delete(full);
            return true;
        });
    }

    public void Copy(string from, string to, bool overwrite)
    {
        var source = Full(from);
        if (!File.Exists(source))
        {
            throw new DeviceActionException("Run.FileNotFound", from);
        }

        var target = Full(to);
        if (!overwrite && File.Exists(target))
        {
            throw new DeviceActionException("Run.FileExists", to);
        }

        Attempt(from, () =>
        {
            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.Copy(source, target, overwrite);
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

    public void Move(string from, string to, bool overwrite)
    {
        var source = Full(from);
        if (!File.Exists(source))
        {
            throw new DeviceActionException("Run.FileNotFound", from);
        }

        var target = Full(to);
        if (!overwrite && File.Exists(target))
        {
            throw new DeviceActionException("Run.FileExists", to);
        }

        Attempt(from, () =>
        {
            var folder = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.Move(source, target, overwrite);
            return true;
        });
    }

    public IReadOnlyList<string> List(string folder, string pattern, bool recurse)
    {
        var full = Full(folder);
        if (!Directory.Exists(full))
        {
            throw new DeviceActionException("Run.FolderNotFound", folder);
        }

        var search = string.IsNullOrWhiteSpace(pattern) ? "*" : pattern;
        var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Attempt(folder, () => Directory.GetFiles(full, search, option));
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
