using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Storage;

// Putting the assets back out beside the package.
public static partial class MacroPackage
{
    /// <summary>Unpacks referenced images next to the package as it is read.</summary>
    private sealed class AssetExtractor
    {
        private readonly ZipArchive _archive;
        private readonly string _folder;
        private readonly bool _reuseExisting;
        private readonly bool _extract;
        private readonly Dictionary<string, string> _mapped = new(StringComparer.OrdinalIgnoreCase);

        public AssetExtractor(ZipArchive archive, string folder, bool reuseExisting,
            bool extract = true)
        {
            _archive = archive;
            _folder = folder;
            _reuseExisting = reuseExisting;
            _extract = extract;
        }

        /// <summary>
        /// A picture that belongs to this folder already: what is there is the one the macro means,
        /// so the file is only written when the folder has nothing under that name.
        /// </summary>
        private string Keep(string name, ZipArchiveEntry entry)
        {
            var target = Path.Combine(_folder, name);
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(_folder);
                entry.ExtractToFile(target, true);
            }

            return target;
        }

        /// <summary>
        /// A picture arriving from somewhere else: the same bytes under the name are shared, and
        /// different ones are written beside it under a number of their own.
        /// </summary>
        private string KeepApart(string name, ZipArchiveEntry entry)
        {
            byte[] arriving;
            using (var source = entry.Open())
            using (var buffer = new MemoryStream())
            {
                source.CopyTo(buffer);
                arriving = buffer.ToArray();
            }

            Directory.CreateDirectory(_folder);
            var stem = Path.GetFileNameWithoutExtension(name);
            var extension = Path.GetExtension(name);
            for (var counter = 1; ; counter++)
            {
                var candidate = counter == 1 ? name : $"{stem}-{counter}{extension}";
                var target = Path.Combine(_folder, candidate);
                if (!File.Exists(target))
                {
                    File.WriteAllBytes(target, arriving);
                    return target;
                }

                if (File.ReadAllBytes(target).SequenceEqual(arriving))
                {
                    return target;
                }
            }
        }

        public string Expand(string value)
        {
            if (!_extract
                || !value.StartsWith(AssetsFolder, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            if (_mapped.TryGetValue(value, out var existing))
            {
                return existing;
            }

            var entry = _archive.GetEntry(value);
            if (entry is null)
            {
                return value;
            }

            // Only the file name is used, so a crafted package cannot escape the folder.
            var name = Path.GetFileName(value);
            var resolved = Path.GetFullPath(_reuseExisting
                ? Keep(name, entry)
                : KeepApart(name, entry));
            _mapped[value] = resolved;
            return resolved;
        }
    }
}
